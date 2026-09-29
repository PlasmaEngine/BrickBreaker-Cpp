#pragma once

#include <BrickBreakerPlugin/Gameplay/BrickBreakerSimulation.h>
#include <Core/Utils/Blackboard.h>
#include <Core/World/World.h>
#include <GameEngine/GameState/GameState.h>
#include <RendererCore/Meshes/MeshResource.h>

class BrickBreakerGameState : public plGameState
{
  PL_ADD_DYNAMIC_REFLECTION(BrickBreakerGameState, plGameState);

public:
  void OnActivation(plWorld* pWorld, plStringView sStartPosition, const plTransform& offset) override;
  void OnDeactivation() override;

  void ProcessInput() override;
  void BeforeWorldUpdate() override;

protected:
  void ConfigureInputActions() override;
  void ConfigureMainCamera() override;
  void SetupMainView(plGALSwapChainHandle swapChain, plSizeU32 renderSize, plSizeU32 windowSize) override;

private:
  void BuildTable();
  void UpdateObjects();

  void CreateHud();
  void UpdateHud();

  plGameObjectHandle AddMesh(plStringView name, const plVec3& position, const plVec3& scale,
    const plColor& color, bool sphere = false, bool emissive = false);

  plUniquePtr<plWorld> m_pOwnedWorld;
  plGameObjectHandle m_hRoot;

  plGameObjectHandle m_hPaddle;
  plGameObjectHandle m_hBall;
  plStaticArray<plGameObjectHandle, plBrickBreakerSimulation::BrickCount> m_Bricks;

  plMeshResourceHandle m_hBoxMesh;
  plMeshResourceHandle m_hBallMesh;

  plBrickBreakerSimulation m_Game;
  plSharedPtr<plBlackboard> m_pHudBoard;

  double m_fAccumulator = 0;
  float m_fInput = 0;

  plUInt32 m_uiReviewFrames = 0;
};
