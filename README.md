# Chat Export Explorer v3: Trajectory Engine

A private local Flask app for searching a ChatGPT export and comparing a current situation with historically similar daily states.

## Upgrade from v2

Copy these into this folder:

- `chat_history.db`
- optionally `conversations.json`

Then run:

```powershell
python -m pip install -r requirements.txt
python build_trajectories.py
python app.py
```

Open `http://127.0.0.1:5000/trajectory`.

## What the forecast means

The engine converts user messages into measurable feature densities such as career, learning, relationship focus, uncertainty, action, distress, confidence, sleep, money, and health. It compares your entered text with historical daily states using cosine similarity, then examines the state 7, 14, or 30 days later.

It reports historical resemblance, not certainty. Every result includes the matching date and source text sample.

## V4: Decision Simulator

Open `/simulate` or click **Simulator** in the navigation. Describe the current state, choose two or more possible actions, and compare what historically followed similar states when those actions appeared in the archive.

The branch score is a transparent heuristic: increases in career, learning, action, and confidence count positively; increases in distress and uncertainty count negatively. Always inspect case counts and source evidence before interpreting a result.
