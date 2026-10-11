# SURVIVOR SOULS - STATE

## GOAL
3D mobile game. Unity. iOS + Android.

## DONE
- 2026-10-11: Unity project made. Editor 6000.6.5f1 (only editor on machine). Template URP blank 17.2.1.
- Player settings: product "Survivor Souls", company "irfancnk", bundle id `com.irfancnk.survivorsouls` (iOS + Android). Android IL2CPP + ARM64. Orientation LandscapeLeft. Active build target Android.
- Git init. Unity .gitignore. .gitattributes (YAML merge rules).

## DECISIONS
- Render pipeline: URP. Reason: mobile perf, good look.
- Git LFS: NOT on. git-lfs not installed. Add later before big binary assets.
- Repo: public, github.com/irfancnk/survivor-souls.

## NEXT
- gh auth login, then make repo + push (if not done).
- Install git-lfs, track binaries.
- Game design doc. Core loop. Folder structure under Assets/_Project.
