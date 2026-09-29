#include <BrickBreakerGame/BrickBreakerGame.h>

#include <Core/Input/InputManager.h>
#include <Foundation/Configuration/Startup.h>
#include <Foundation/IO/FileSystem/FileSystem.h>
#include <Foundation/Logging/Log.h>
#include <Texture/Image/Image.h>

// this injects the C++ main() function
PL_APPLICATION_ENTRY_POINT(BrickBreakerGame);

BrickBreakerGame::BrickBreakerGame()
  : plGameApplication("BrickBreaker", nullptr)
{
}

BrickBreakerGame::~BrickBreakerGame()
{
  plGlobalLog::RemoveLogWriter(m_ReviewLogSubscription);
}

plResult BrickBreakerGame::TryProjectFolder(plStringView sPath)
{
  plStringBuilder sProjDir = sPath;
  sProjDir.MakeCleanPath();

  plStringBuilder sProjFile;
  sProjFile.SetPath(sProjDir, "plProject");

  if (sProjFile.IsAbsolutePath() && plOSFile::ExistsFile(sProjFile))
  {
    m_sAppProjectPath = sProjDir;
    return PL_SUCCESS;
  }

  return PL_FAILURE;
}

void BrickBreakerGame::DetermineProjectPath()
{
  const auto* commandLine = plCommandLineUtils::GetGlobalInstance();
  if (commandLine->HasOption("-project") && TryProjectFolder(commandLine->GetStringOption("-project")).Succeeded())
    return;

    // IMPORTANT!
    //
    // The project path has to be set for the plGameApplication to know where the main 'project' data directory is.
    // Without it, nothing will work (the game plugin won't be loaded etc).
    //
    // The path can be relative to the '>SDK' directory (the root folder where PL is located).
    // It may also be absolute (though this isn't portable across machines).
    // Or it can be relative to plOSFile::GetApplicationDirectory() (where the Game.exe is).
    //
    // If your project is inside the PL directory, use a relative path from there.
    // If it is somewhere outside, you either need to use an absolute path or some other way to locate it.
    //
    // Note that in a final exported build the project folder is always merged with the PL data folders into one package.

#if PL_ENABLED(PL_PLATFORM_ANDROID)
  m_sAppProjectPath = plStringBuilder(plFileSystem::GetSdkRootDirectory(), "/Data/project");
  return;
#endif

  // this path works for exported projects, because during export the project folder is always copied there
  plStringBuilder sProjDir;
  if (plFileSystem::ResolveSpecialDirectory(">sdk/Data/project", sProjDir).Succeeded())
  {
    if (TryProjectFolder(sProjDir).Succeeded())
      return;
  }

#ifdef GAME_PROJECT_FOLDER
  // this absolute path will only work on the machine where the game is compiled,
  // but it works for projects that are located outside the plEngine folder
  if (TryProjectFolder(PL_PP_STRINGIFY(GAME_PROJECT_FOLDER)).Succeeded())
    return;
#endif

  // in other cases, try this relative path
  m_sAppProjectPath = "Data/Tools/plEditor/ProjectTemplates/BrickBreaker-Cpp";
}

plUniquePtr<plGameStateBase> BrickBreakerGame::CreateGameState()
{
  // usually we should only have a single non-fallback gamestate which is automatically picked
  // but if necessary, we can override this here
  return SUPER::CreateGameState();
}

plResult BrickBreakerGame::BeforeCoreSystemsStartup()
{
  plStartup::AddApplicationTag("game");

  PL_SUCCEED_OR_RETURN(SUPER::BeforeCoreSystemsStartup());

  DetermineProjectPath();

  const auto* commandLine = plCommandLineUtils::GetGlobalInstance();
  if (commandLine->HasOption("-reviewLog") && m_ReviewLog.Open(commandLine->GetStringOption("-reviewLog"), plFileOpenMode::Write).Succeeded())
  {
    m_ReviewLogSubscription = plGlobalLog::AddLogWriter(plMakeDelegate(&BrickBreakerGame::OnReviewLog, this));
  }

  return PL_SUCCESS;
}

void BrickBreakerGame::OnReviewLog(const plLoggingEventData& event)
{
  if (!event.m_sText.IsEmpty())
    m_ReviewLog.Write(event.m_sText.GetStartPointer(), event.m_sText.GetElementCount()).IgnoreResult();

  m_ReviewLog.Write("\n", 1).IgnoreResult();
}

void BrickBreakerGame::AfterCoreSystemsStartup()
{
  ExecuteInitFunctions();

  plStartup::StartupHighLevelSystems();

  ActivateGameState(nullptr, {}, plTransform::MakeIdentity());
}

void BrickBreakerGame::Run_InputUpdate()
{
  SUPER::Run_InputUpdate();

  // Optional unattended visual smoke test, using the engine's own frame capture.
  const auto* commandLine = plCommandLineUtils::GetGlobalInstance();
  const plUInt32 uiCaptureFrame = static_cast<plUInt32>(plMath::Max(1, commandLine->GetIntOption("-reviewFrames", 240)));
  if (commandLine->HasOption("-reviewScreenshot") && ++m_uiReviewFrames == uiCaptureFrame)
    TakeScreenshot();

  if (auto pGameState = GetActiveGameState())
  {
    // pass through the closing of the application
    if (pGameState->WasQuitRequested())
    {
      RequestQuit();
    }
  }
}

void BrickBreakerGame::StoreScreenshot(plImage&& image, plStringView sContext)
{
  const auto* commandLine = plCommandLineUtils::GetGlobalInstance();
  if (commandLine->HasOption("-reviewScreenshot"))
  {
    if (image.SaveTo(commandLine->GetStringOption("-reviewScreenshot")).Failed())
      SetReturnCode(1);

    RequestQuit();
    return;
  }

  SUPER::StoreScreenshot(std::move(image), sContext);
}
