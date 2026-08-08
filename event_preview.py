import sqlite3
import random
import statistics
from event_discovery import segment_all


def fetch_events_stats(conn):
    cur = conn.cursor()
    cur.execute('SELECT COUNT(*) FROM events')
    total = cur.fetchone()[0]
    cur.execute('SELECT message_count FROM events')
    counts = [r[0] for r in cur.fetchall()]
    mean = statistics.mean(counts) if counts else 0
    median = statistics.median(counts) if counts else 0
    singletons = sum(1 for c in counts if c == 1)
    singleton_pct = 100.0 * singletons / total if total else 0
    # role composition
    cur.execute('SELECT event_id, user_message_count, assistant_message_count FROM events')
    only_user = 0
    only_assist = 0
    mixed = 0
    for eid, u, a in cur.fetchall():
        if u > 0 and a == 0:
            only_user += 1
        elif a > 0 and u == 0:
            only_assist += 1
        else:
            mixed += 1
    return {
        'total_events': total,
        'mean_messages_per_event': mean,
        'median_messages_per_event': median,
        'singleton_pct': singleton_pct,
        'only_user': only_user,
        'only_assist': only_assist,
        'mixed': mixed
    }


def print_event_detail(conn, event_id):
    cur = conn.cursor()
    cur.execute('SELECT conversation_id, start_time, end_time, message_count, user_message_count, assistant_message_count, rep_user_message_id, rep_assistant_message_id FROM events WHERE event_id=?', (event_id,))
    r = cur.fetchone()
    if not r:
        print('Event not found', event_id)
        return
    conversation_id, start_time, end_time, message_count, user_count, assist_count, rep_user, rep_assist = r
    print('\nEVENT', event_id)
    print('conversation:', conversation_id)
    print('time:', start_time, '->', end_time)
    print('counts: total', message_count, 'user', user_count, 'assistant', assist_count)
    if rep_user:
        print('rep_user_message_id:', rep_user)
    if rep_assist:
        print('rep_assistant_message_id:', rep_assist)
    # fetch messages
    cur.execute('''SELECT m.message_id, m.role, m.created_at, m.text
                   FROM event_messages em JOIN messages m ON em.message_id=m.message_id
                   WHERE em.event_id=? ORDER BY em.seq_in_event''', (event_id,))
    for mid, role, created_at, text in cur.fetchall():
        print('\n- ', role, created_at, mid)
    try:
        safe = text.encode('ascii','backslashreplace').decode('ascii')
    except Exception:
        safe = repr(text)
    print(safe)


if __name__ == '__main__':
    conn = sqlite3.connect('chat_history.db')
    conn.row_factory = None
    print('Segmenting corpus into events (SIM_THRESHOLD=0.6) ...')
    total_events = segment_all(conn, sim_threshold=0.6)
    print('Segmentation complete. Events created:', total_events)
    stats = fetch_events_stats(conn)
    print('\nStatistics:')
    for k,v in stats.items():
        print(f'  {k}: {v}')
    # random samples
    cur = conn.cursor()
    cur.execute('SELECT event_id FROM events')
    all_eids = [r[0] for r in cur.fetchall()]
    sample_20 = random.sample(all_eids, min(20, len(all_eids)))
    print('\n20 random sample events (detailed):')
    for eid in sample_20:
        print_event_detail(conn, eid)
        print('\n' + '='*60)
    # 10 largest events
    cur.execute('SELECT event_id, message_count FROM events ORDER BY message_count DESC LIMIT 10')
    print('\n10 largest events:')
    for eid, c in cur.fetchall():
        print('-', eid, 'messages=', c)
    # 10 shortest non-singleton events
    cur.execute('SELECT event_id, message_count FROM events WHERE message_count>1 ORDER BY message_count ASC LIMIT 10')
    print('\n10 shortest non-singleton events:')
    for eid, c in cur.fetchall():
        print('-', eid, 'messages=', c)
    conn.close()
