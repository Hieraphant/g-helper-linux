TANGOR-AUDIT — tracking directory for the Tangor fork (FA617NT)
================================================================

Keep-track directory. Plain-text records so you don't have to scroll CLI history.

FILES HERE
  feature-audit.txt   Complete feature audit — every capability g-helper detects on
                      this machine, cross-checked against our coverage. The definitive
                      "nothing forgotten" checklist.

COMPANION DOCS (markdown, in repo root)
  TANGOR-TRACKER.md   The status board + NEXT UP priority list (what's left to do).
  TANGOR-FINDINGS.md  The WHY — every on-metal finding (asus PPT cosmetic, stapm
                      firmware-locked, clobber behaviour, GPU facts, etc.).
  TANGOR-NOTES.md     Onboarding / quick orientation.

ONE-LINE STATUS
  Reverse-engineering DONE. ASUS firmware can't override custom values (RyzenAdj writes
  the real SMU + a reinforcement timer out-stubborns every clobber). Remaining items are
  features/polish on the tracker, not unknowns.
