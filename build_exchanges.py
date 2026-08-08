import sqlite3
import time
from collections import defaultdict, Counter
import random
from datetime import datetime
try:
    import numpy as np
except Exception as e:
    print('Numpy is required:', e)
    raise

DB='chat_history.db'
MODEL_NAME='all-MiniLM-L6-v2'

def load_embeddings(conn):
    cur=conn.cursor()
    cur.execute("SELECT message_id, embedding, dim FROM message_embeddings WHERE model_name=?", (MODEL_NAME,))
    emap={}
    for mid, blob, dim in cur.fetchall():
        if blob is None:
            continue
        arr = np.frombuffer(blob, dtype=np.float32)
        if arr.size != dim:
            # try reshape
            arr = arr.astype(np.float32)
        emap[mid]=arr
    return emap


def ensure_schema(conn):
    cur=conn.cursor()
    cur.execute('''
    CREATE TABLE IF NOT EXISTS exchanges (
        exchange_id TEXT PRIMARY KEY,
        conversation_id TEXT,
        start_time TEXT,
        end_time TEXT,
        user_message_count INTEGER,
        assistant_message_count INTEGER,
        user_centroid BLOB,
        context_centroid BLOB,
        dim INTEGER,
        norm REAL,
        model_name TEXT,
        representative_user_message_id TEXT,
        created_at TEXT
    )
    ''')
    cur.execute('''
    CREATE TABLE IF NOT EXISTS exchange_messages (
        exchange_id TEXT,
        message_id TEXT,
        role TEXT,
        seq_in_exchange INTEGER,
        PRIMARY KEY (exchange_id, message_id)
    )
    ''')
    conn.commit()


def build_exchanges(conn, emap):
    cur=conn.cursor()
    # wipe previous runs
    cur.execute('DELETE FROM exchange_messages')
    cur.execute('DELETE FROM exchanges')
    conn.commit()

    # load messages ordered
    cur.execute('SELECT message_id, conversation_id, created_at, role, text FROM messages ORDER BY conversation_id, created_at')
    rows=cur.fetchall()
    convs=defaultdict(list)
    for message_id, conversation_id, created_at, role, text in rows:
        convs[conversation_id].append({'message_id':message_id,'created_at':created_at,'role':role,'text':text})

    total_exchanges=0
    exchange_rows=[]
    exchange_msg_rows=[]
    role_sequence_counter=Counter()
    convs_begin_assistant=0
    consecutive_assistant_runs=0
    consecutive_user_runs=0
    exchanges_with_no_user=0
    unusually_large=[]

    for conv_id, msgs in convs.items():
        if not msgs:
            continue
        # count if conversation begins with assistant
        if msgs[0]['role']=='assistant':
            convs_begin_assistant += 1
        n=len(msgs)
        buf=[]
        for idx in range(n):
            m=msgs[idx]
            buf.append(m)
            lookahead = msgs[idx+1] if idx+1<n else None
            curr_role = m['role']
            next_role = lookahead['role'] if lookahead else None
            # decide finalize
            finalize=False
            if lookahead is None:
                finalize=True
            else:
                if curr_role=='assistant' and next_role=='user':
                    finalize=True
                elif curr_role=='user' and next_role=='assistant':
                    finalize=False
                elif curr_role=='user' and next_role=='user':
                    finalize=False
                elif curr_role=='assistant' and next_role=='assistant':
                    finalize=False
                else:
                    finalize=False
            if finalize:
                # build exchange from buf
                total_exchanges += 1
                ex_id = str(time.time()).replace('.','') + '-' + str(random.randint(1000,9999))
                start_time = buf[0]['created_at']
                end_time = buf[-1]['created_at']
                user_msgs = [m for m in buf if m['role']=='user']
                assistant_msgs = [m for m in buf if m['role']=='assistant']
                umc = len(user_msgs)
                amc = len(assistant_msgs)
                # collect embeddings
                user_embs = [emap[m['message_id']] for m in user_msgs if m['message_id'] in emap]
                all_embs = [emap[m['message_id']] for m in buf if m['message_id'] in emap]
                dim = None
                user_centroid_blob = None
                context_centroid_blob = None
                norm = None
                rep_user_id = None
                if all_embs:
                    context_centroid = np.mean(np.stack(all_embs), axis=0).astype(np.float32)
                    dim = context_centroid.size
                    norm = float(np.linalg.norm(context_centroid))
                    context_centroid_blob = context_centroid.tobytes()
                if user_embs:
                    user_centroid = np.mean(np.stack(user_embs), axis=0).astype(np.float32)
                    user_centroid_blob = user_centroid.tobytes()
                    # representative user message: nearest to user_centroid
                    best_sim = -1.0
                    best_mid = None
                    u_norm = np.linalg.norm(user_centroid)
                    for m in user_msgs:
                        mid = m['message_id']
                        if mid not in emap:
                            continue
                        v = emap[mid]
                        dot = float(np.dot(user_centroid, v))
                        denom = (u_norm * np.linalg.norm(v) + 1e-12)
                        sim = dot/denom
                        if sim>best_sim:
                            best_sim=sim; best_mid=mid
                    rep_user_id = best_mid
                # record role sequence
                seq = ''.join(['U' if m['role']=='user' else 'A' for m in buf])
                role_sequence_counter[seq]+=1
                if umc==0:
                    exchanges_with_no_user += 1
                if len(buf)>50:
                    unusually_large.append((ex_id,len(buf), start_time, end_time))
                # store exchange
                exchange_rows.append((ex_id, conv_id, start_time, end_time, umc, amc, user_centroid_blob, context_centroid_blob, dim, norm, MODEL_NAME, rep_user_id, datetime.utcnow().isoformat()+"Z"))
                # store exchange messages
                for seqi, m2 in enumerate(buf):
                    exchange_msg_rows.append((ex_id, m2['message_id'], m2['role'], seqi))
                # reset buffer
                buf=[]
        # done conversation
    # bulk insert
    cur = conn.cursor()
    cur.executemany('INSERT INTO exchanges(exchange_id, conversation_id, start_time, end_time, user_message_count, assistant_message_count, user_centroid, context_centroid, dim, norm, model_name, representative_user_message_id, created_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)', exchange_rows)
    cur.executemany('INSERT INTO exchange_messages(exchange_id, message_id, role, seq_in_exchange) VALUES (?,?,?,?)', exchange_msg_rows)
    conn.commit()

    # reports
    all_msg_counts = [um+am for (_,_,_,_,um,am,_,_,_,_,_,_,_) in exchange_rows]
    user_counts = [um for (_,_,_,_,um,am,_,_,_,_,_,_,_) in exchange_rows]
    assistant_counts = [am for (_,_,_,_,um,am,_,_,_,_,_,_,_) in exchange_rows]
    import statistics
    total = len(exchange_rows)
    mean_msgs = statistics.mean(all_msg_counts) if all_msg_counts else 0
    median_msgs = statistics.median(all_msg_counts) if all_msg_counts else 0
    user_only = sum(1 for (_,_,_,_,um,am,_,_,_,_,_,_,_) in exchange_rows if um>0 and am==0)
    assistant_only = sum(1 for (_,_,_,_,um,am,_,_,_,_,_,_,_) in exchange_rows if am>0 and um==0)
    mixed = sum(1 for (_,_,_,_,um,am,_,_,_,_,_,_,_) in exchange_rows if um>0 and am>0)

    print('TOTAL EXCHANGES:', total)
    print('MEAN messages/exchange:', mean_msgs)
    print('MEDIAN messages/exchange:', median_msgs)
    print('USER-ONLY exchanges:', user_only)
    print('ASSISTANT-ONLY orphan exchanges:', assistant_only)
    print('MIXED exchanges:', mixed)

    # distributions
    def dist_hist(arr):
        c=Counter(arr)
        for k in sorted(c.keys()):
            print(f'  {k}: {c[k]}')
    print('\nDISTRIBUTION of user messages per exchange:')
    dist_hist(user_counts)
    print('\nDISTRIBUTION of assistant messages per exchange:')
    dist_hist(assistant_counts)

    # 30 random exchanges
    print('\n30 RANDOM EXCHANGES:')
    sample = random.sample(exchange_rows, min(30, len(exchange_rows)))
    for ex in sample:
        ex_id = ex[0]
        print('\nEXCHANGE', ex_id, 'conv', ex[1], 'start', ex[2], 'end', ex[3], 'u_msgs', ex[4], 'a_msgs', ex[5])
        cur=conn.cursor()
        cur.execute('SELECT em.role, m.created_at, m.message_id, m.text FROM exchange_messages em JOIN messages m ON em.message_id=m.message_id WHERE em.exchange_id=? ORDER BY em.seq_in_exchange', (ex_id,))
        for role, created_at, mid, text in cur.fetchall():
            safe=(text or '').encode('ascii','backslashreplace').decode('ascii')
            print(' -', role, created_at, mid)
            print('   ', safe[:500].replace('\n',' '))

    # 20 largest exchanges
    print('\n20 LARGEST EXCHANGES:')
    largest = sorted(exchange_rows, key=lambda r: (r[4]+r[5]), reverse=True)[:20]
    for ex in largest:
        print(' EXCHANGE', ex[0], 'size', ex[4]+ex[5], 'u',ex[4],'a',ex[5], 'conv', ex[1])

    # malformed patterns
    print('\nMALFORMED SEQUENCES / PATTERNS:')
    print(' Conversations beginning with assistant:', convs_begin_assistant)
    # consecutive runs counts
    # compute consecutive user runs and assistant runs across corpus
    consec_user=0
    consec_assist=0
    for conv_id, msgs in convs.items():
        prev=None
        for m in msgs:
            if prev is not None:
                if prev=='assistant' and m['role']=='assistant':
                    consec_assist +=1
                if prev=='user' and m['role']=='user':
                    consec_user +=1
            prev = m['role']
    print(' Consecutive assistant->assistant transitions:', consec_assist)
    print(' Consecutive user->user transitions:', consec_user)
    print(' Exchanges with no user message:', exchanges_with_no_user)
    print(' Unusually large exchanges (>50 messages):', len(unusually_large))

    # role-sequence patterns and counts
    print('\nROLE-SEQUENCE PATTERNS (top 30):')
    for seq, cnt in role_sequence_counter.most_common(30):
        print(' ', seq, cnt)

    conn.commit()

if __name__=='__main__':
    conn=sqlite3.connect(DB)
    ensure_schema(conn)
    print('Loading embeddings...')
    emap = load_embeddings(conn)
    print('Embeddings loaded:', len(emap))
    build_exchanges(conn, emap)
    conn.close()
