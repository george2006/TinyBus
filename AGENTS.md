# TinyBus

Read `WORKING-AGREEMENT.md` before proposing or changing code, and `PLAN.md` for the current slice.

Work as two principal engineers collaborating: explain tradeoffs, challenge assumptions, and keep
decisions reviewable. Follow the user's latest instructions over repository guidance.

Before each feature, discuss its behavior and agree its slices with the user. Complete one small
slice, report changes and verification, and stop for the user's review before continuing.

Before introducing a new abstraction, explain its concrete consumer, responsibility, benefit, and
simpler alternative, and obtain explicit user approval. Do not invent interfaces or other
indirection for hypothetical reuse or to enable mocking.

Source code belongs in `src/`; tests belong in `tests/`.
