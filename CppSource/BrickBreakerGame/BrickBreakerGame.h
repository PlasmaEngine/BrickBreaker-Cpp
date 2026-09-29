#pragma once

#include <Foundation/IO/OSFile.h>
#include <Foundation/Logging/Log.h>
#include <GameEngine/GameApplication/GameApplication.h>

class BrickBreakerGame : public plGameApplication
{
public:
  using SUPER = plGameApplication;

  BrickBreakerGame();
  ~BrickBreakerGame();

protected:
  virtual void Run_InputUpdate() override;

  virtual plResult BeforeCoreSystemsStartup() override;
  virtual void AfterCoreSystemsStartup() override;

  virtual plUniquePtr<plGameStateBase> CreateGameState() override;
  void StoreScreenshot(plImage&& image, plStringView sContext = {}) override;

private:
  plResult TryProjectFolder(plStringView sPath);
  void DetermineProjectPath();

  void OnReviewLog(const plLoggingEventData& event);

  plUInt32 m_uiReviewFrames = 0;

  plOSFile m_ReviewLog;
  plEventSubscriptionID m_ReviewLogSubscription = 0;
};
