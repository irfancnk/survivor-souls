# HUD feature styles (KAN-228)

Put the USS rules of a NEW HUD feature in its own file here: `<Feature>.uss` (e.g. `ComboMeter.uss`).
`Editor/SceneSteps/HudFeatureStylesStep` adds every `.uss` in this folder to the HUD (after GameHud.uss, sorted by path)
on the next Game scene rebuild. Do not add new rule blocks to `../GameHud.uss`: 5 parallel routines conflict there.
New HUD elements: build them in code in the feature's own component (or clone an own UXML under the HUD root);
do not add them to `../GameHud.uxml`.
