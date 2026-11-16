# Brick Breaker — C++

A playable 3D Brick Breaker with the same table, controls and rules as the Visual Script, C# and AngelScript versions. The 24 bricks, ball, paddle, rails, lights, and camera are created in C++. All artwork is procedural; no external art pack or physics plugin is required.

## Requirements

Windows, Plasma Engine 0.12.1, a C++ toolchain supported by PlasmaBuild (the Visual Studio C++ build tools), and `plasmabuild` on PATH. The Plasma Launcher's **Dependencies** page installs the last two.

Get the project from the Plasma Launcher's **Samples** page (BrickBreaker, C++), or clone this repository and use **Add Existing Project**. The first time the editor opens it, it offers to install GameUI 1.4.1, which is listed in `Editor/Packages.ddl`. Accept, and it is installed into the project's `Plugins` folder, where the C++ build finds its headers and libraries.

## Build and play

```powershell
.\Build.ps1 -Test -Run
```

Or double-click `Play.cmd`. The script generates a Visual Studio workspace, builds the plugin and game, optionally runs the mechanics tests, transforms this project's assets through EditorProcessor, and launches the game. Use `-SkipAssetTransform` only for code-only iterations after a successful initial asset transform.

The script uses the engine the launcher assigned to this project; pass `-SdkRoot 'C:\path\to\engine'` to use another. It uses the GameUI package the editor installed; pass `-GameUiRoot` with a built package or source checkout instead. `Configuration` must match the engine binaries (Development by default).

The executable is `Binaries/Windows_Development/BrickBreakerGame.exe` in the engine folder, and the GameUI runtime DLL is copied beside it. `Build.ps1 -Run` passes `-project` with this folder so the game loads this copy. Close the running game before rebuilding.

## Controls and objective

| Action | Control |
| --- | --- |
| Move paddle | A / D or left / right arrows |
| Launch docked ball | Space or left click |
| Pause / resume | P or Escape |
| Restart entire round | R |
| Exit standalone window | Close window or Alt+F4 |

Clear all 24 bricks for 2,400 points. A missed ball consumes one of three lives; surviving bricks and score remain until restart. Each brick increases ball speed slightly, up to a cap. A new life restores the initial ball speed. Contact near the paddle edges sends the ball further sideways; a central contact sends it straight up the table.

## Where to make changes

- `CppSource/BrickBreakerPlugin/Gameplay/BrickBreakerSimulation.h`: layout, speeds, lives, scoring, swept collisions, and round state. The fixed 120 Hz simulation is independent of rendering.
- `CppSource/BrickBreakerPlugin/GameState/BrickBreakerGameState.cpp`: procedural 3D table, palette, lights, camera, input, and the GameUI blackboard values.
- `RenderPipelines/BrickBreaker.plRenderPipelineAsset`: editable forward render graph, including the GUI rendering stage.
- `UI/BrickBreakerHUD.plUiDocumentAsset`: anchored GameUI panels, text, counters, and prompt bindings.
- `UI/HudData.plBlackboardTemplateAsset`: HUD binding contract and editor preview defaults.
- `UI/HudFont.plUiFontAsset`: font baked from the engine's PT Root UI font.
- `Materials/`: plain engine materials used by the generated meshes.
- `CppSource/BrickBreakerTests/BrickBreakerTests.cpp`: mechanics regression checks and a reproducible complete game.

First exercise: change `PaddleSpeed` from 16 to 12, rebuild, and compare control. Change the three `RowColors` in the game state to explore a different art direction.

## Things to know

- `Scenes/Main.plScene` is an empty launch scene: the game state builds the table when the game runs, so the editor shows nothing until you play. The other language versions use an authored scene instead.
- The plugin and game keep fixed `BrickBreaker` names and build into the engine's `Binaries` folder, so build one copy of this project at a time.
- Physics is deliberately kinematic: the ball is swept against expanded box bounds on the horizontal table plane. This avoids a physics package dependency, but brick corners use conservative box bounds rather than exact sphere/rounded-corner contacts.
- `SetupMainView` loads the transformed BrickBreaker pipeline by asset UUID. Passes and connections are authored in the pipeline asset; no render graph is constructed by the game. A `plUiCanvasComponent` loads the HUD document, and gameplay publishes score, lives, remaining bricks, and prompt values to a blackboard. Layout uses a reference height of 900 with viewport anchors.

## Screenshot capture

The game accepts `-reviewScreenshot :bin/BrickBreakerReview.png` to capture after 240 frames and exit; the image lands in the engine's binary folder. Override the delay with `-reviewFrames 1800`. Optional `-reviewLog` takes an absolute log path. With `-reviewScreenshot`, `-reviewState playing|paused|won|lost` applies a HUD fixture at frame 120 so the capture shows that state.
