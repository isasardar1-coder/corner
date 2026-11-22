# Corner for macOS: the model in Swift

`CornerCore` is the platform-neutral part of Corner (tasks, the sleep log, the energy curve, Plan, check-ins,
the sleep-need estimate and the completion log), ported line by line from the Windows app's C# model.
It uses Foundation only. The data files are the same as on Windows (`tasks.json`, `settings.json`, `sleep.json`,
`routines.json`, `done.csv`, `days.csv`, `checkins.csv`, `profile.txt`), so they can be copied between a PC and a Mac.
They live in `~/Library/Application Support/Corner`, or in the folder named by the `CORNER_DATA` environment variable.

`CornerChecks` is the port of the Windows checks (78 of them, same names and order).

## Running the checks

Needs Swift 5.9 or later (Xcode 15+ on macOS).

```sh
cd swift
bash run-checks.sh          # prints PASS/FAIL per check, then ALL PASS; exit code 1 on any failure
bash run-checks.sh --diff   # the same, then a diff against Tests/reference-output.txt
bash run-checks.sh sim      # the sleep-need simulation
```

The script copies `Tests/data/sleep.json` and `Tests/data/checkins.csv` (invented data, not anyone's real log)
into a temporary data folder, because some checks read them. `Tests/reference-output.txt` is what the C# checks
print on that same data. The last line before `ALL PASS` ("your own data: ...") depends on today's date, since the
sleep-need estimate only looks at the last 60 days, so it may differ from the reference.
