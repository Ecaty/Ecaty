# Windows local AI pack

Run `python Tools/LocalAI/setup.py` from any folder. Requires Python 3.10+, internet and about 4 GB free space. Pins/checksums are in the script and installed UPSTREAM.json. Downloads stay in ignored LocalData/AI; no accounts or API charges. Existing custom config.json is preserved on rerun. The official public Whisper test sample is placed in ignored Logs/LocalAiFixture.

Unity Play -> account -> On device -> Start. Microphone -> stop -> review draft -> send.

For a Windows player: `python Tools/LocalAI/stage.py Build/Ecaty.exe`. Alternatively launch with `-localAiRoot <absolute pack directory>`.

Actual local inference test: Ecaty > Verification > Verify local AI and speech. Protocol/dictation test: Verify local AI protocols and dictation. Neither automatically records your microphone.

Read ../../docs/LOCAL-AI.md for model replacement, performance, privacy, current test coverage and mobile limitations. Nothing here promises frontier-cloud parity or a completed safety sandbox.
