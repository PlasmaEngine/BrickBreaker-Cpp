#include <BrickBreakerPlugin/BrickBreakerPluginPCH.h>

#include <BrickBreakerPlugin/GameState/BrickBreakerGameState.h>
#include <Core/Graphics/Geometry.h>
#include <Foundation/Time/Clock.h>
#include <Foundation/Utilities/CommandLineUtils.h>
#include <GameUiPlugin/Components/UiCanvasComponent.h>
#include <RendererCore/Lights/AmbientLightComponent.h>
#include <RendererCore/Lights/DirectionalLightComponent.h>
#include <RendererCore/Material/MaterialResource.h>
#include <RendererCore/Meshes/MeshComponent.h>
#include <RendererCore/Meshes/MeshResourceDescriptor.h>
#include <RendererCore/Pipeline/RenderPipelineResource.h>
#include <RendererCore/Pipeline/View.h>

PL_BEGIN_DYNAMIC_REFLECTED_TYPE(BrickBreakerGameState, 1, plRTTIDefaultAllocator<BrickBreakerGameState>)
PL_END_DYNAMIC_REFLECTED_TYPE;

namespace
{
  using Game = plBrickBreakerSimulation;

  const plColor Mint(0.08f, 0.85f, 0.65f);
  const plColor Ink(0.014f, 0.022f, 0.047f);
  const plColor RowColors[] = {plColor(0.04f, 0.62f, 0.88f), plColor(0.40f, 0.16f, 0.90f), plColor(1.0f, 0.26f, 0.10f)};

  plMeshResourceHandle CreateMesh(const char* name, bool sphere)
  {
    plGeometry geometry;
    if (sphere)
      geometry.AddStackedSphere(1.0f, 32, 16);
    else
      geometry.AddBox(plVec3(1.0f), true);

    geometry.ComputeTangents();

    plMeshResourceDescriptor descriptor;
    descriptor.MeshBufferDesc().AddCommonStreams();
    descriptor.MeshBufferDesc().AllocateStreamsFromGeometry(geometry, plGALPrimitiveTopology::Triangles);
    descriptor.AddSubMesh(geometry.CalculateTriangleCount(), 0, 0);
    descriptor.SetMaterial(0, "Materials/BrickBreaker.plMaterial");
    descriptor.ComputeBounds();

    return plResourceManager::GetOrCreateResource<plMeshResource>(name, std::move(descriptor), name);
  }

  void RegisterAction(const char* name, const char* key, const char* alternate = "")
  {
    plInputActionConfig config;
    config.m_bApplyTimeScaling = false;
    config.m_sInputSlotTrigger[0] = key;
    config.m_sInputSlotTrigger[1] = alternate;

    plInputManager::SetInputActionConfig("BrickBreaker", name, config, true);
  }
} // namespace

void BrickBreakerGameState::OnActivation(plWorld* pWorld, plStringView sStartPosition, const plTransform& offset)
{
  m_Game.Reset();
  m_fAccumulator = 0;
  m_uiReviewFrames = 0;

  if (pWorld == nullptr)
  {
    plWorldDesc description("Brick Breaker");
    m_pOwnedWorld = PL_DEFAULT_NEW(plWorld, description);
    pWorld = m_pOwnedWorld.Borrow();
  }

  SUPER::OnActivation(pWorld, sStartPosition, offset);

  PL_LOCK(m_pMainWorld->GetWriteMarker());

  BuildTable();
  CreateHud();

  UpdateObjects();
  UpdateHud();
}

void BrickBreakerGameState::OnDeactivation()
{
  if (m_pMainWorld != nullptr)
  {
    PL_LOCK(m_pMainWorld->GetWriteMarker());
    m_pMainWorld->DeleteObjectNow(m_hRoot);
  }

  m_pHudBoard.Clear();
  SUPER::OnDeactivation();

  m_pMainWorld = nullptr;
  m_pOwnedWorld.Clear();
}

void BrickBreakerGameState::ConfigureMainCamera()
{
  m_MainCamera.SetCameraMode(plCameraMode::PerspectiveFixedFovY, 48.0f, 0.1f, 200.0f);

  // X is paddle travel, Y is up the table, Z is elevation.
  m_MainCamera.LookAt(plVec3(0, -22.0f, 30.0f), plVec3(0, 0, 0), plVec3(0, 0, 1));
}

void BrickBreakerGameState::SetupMainView(plGALSwapChainHandle swapChain, plSizeU32 renderSize, plSizeU32 windowSize)
{
  SUPER::SetupMainView(swapChain, renderSize, windowSize);

  plView* view;
  if (plRenderWorld::TryGetView(m_hMainView, view))
  {
    view->SetRenderPipelineResource(plResourceManager::LoadResource<plRenderPipelineResource>(
      "{ daa7406b-5d8c-56f8-912e-fa19e946a007 }")); // RenderPipelines/BrickBreaker.plRenderPipelineAsset
  }
}

void BrickBreakerGameState::ConfigureInputActions()
{
  RegisterAction("Left", plInputSlot_KeyA, plInputSlot_KeyLeft);
  RegisterAction("Right", plInputSlot_KeyD, plInputSlot_KeyRight);
  RegisterAction("Launch", plInputSlot_KeySpace, plInputSlot_MouseButton0);
  RegisterAction("Pause", plInputSlot_KeyP, plInputSlot_KeyEscape);
  RegisterAction("Restart", plInputSlot_KeyR);
}

void BrickBreakerGameState::ProcessInput()
{
  SUPER::ProcessInput();

  float left = 0, right = 0;
  plInputManager::GetInputActionState("BrickBreaker", "Left", &left);
  plInputManager::GetInputActionState("BrickBreaker", "Right", &right);
  m_fInput = left - right;

  if (plInputManager::GetInputActionState("BrickBreaker", "Restart") == plKeyState::Pressed)
  {
    m_Game.Reset();
    m_fAccumulator = 0;
  }

  if (plInputManager::GetInputActionState("BrickBreaker", "Pause") == plKeyState::Pressed)
  {
    m_Game.m_bPaused = !m_Game.m_bPaused;
    m_fAccumulator = 0;
  }

  if (plInputManager::GetInputActionState("BrickBreaker", "Launch") == plKeyState::Pressed)
    m_Game.Launch();
}

void BrickBreakerGameState::BeforeWorldUpdate()
{
  if (!m_pMainWorld)
    return;

  PL_LOCK(m_pMainWorld->GetWriteMarker());

  // Capture-only fixtures change the board mid-run to exercise live GameUI bindings.
  const auto* commandLine = plCommandLineUtils::GetGlobalInstance();
  if (commandLine->HasOption("-reviewScreenshot") && ++m_uiReviewFrames == 120)
  {
    const auto scenario = commandLine->GetStringOption("-reviewState");
    if (scenario == "playing" || scenario == "paused" || scenario == "won" || scenario == "lost")
    {
      m_Game.m_uiScore = scenario == "won" ? 2400 : 700;
      m_Game.m_uiLives = scenario == "lost" ? 0 : 2;

      for (plUInt32 i = 0; i < m_Game.m_Bricks.GetCount(); ++i)
        m_Game.m_Bricks[i].m_bAlive = scenario != "won" && i >= 7;

      m_Game.m_State = scenario == "won" ? Game::State::Won : scenario == "lost" ? Game::State::Lost : Game::State::Playing;
      m_Game.m_bPaused = scenario == "paused";
    }
  }

  // Bound catch-up after a stall; never launch the ball across the table in one
  // giant step. The simulation uses the same 120 Hz step at every render rate.
  if (!m_Game.m_bPaused)
  {
    m_fAccumulator += plMath::Min(plClock::GetGlobalClock()->GetTimeDiff().GetSeconds(), 0.1);

    while (m_fAccumulator >= Game::FixedStep)
    {
      m_Game.Step(m_fInput);
      m_fAccumulator -= Game::FixedStep;
    }
  }

  UpdateObjects();
  UpdateHud();
}

plGameObjectHandle BrickBreakerGameState::AddMesh(plStringView name, const plVec3& position, const plVec3& scale,
  const plColor& color, bool sphere, bool emissive)
{
  plGameObjectDesc description;
  description.m_sName.Assign(name);
  description.m_hParent = m_hRoot;
  description.m_bDynamic = true;
  description.m_Tags.Set(plTagRegistry::GetGlobalRegistry().RegisterTag("CastShadow"));
  description.m_LocalPosition = position;
  description.m_LocalScaling = scale;

  plGameObject* object;
  auto handle = m_pMainWorld->CreateObject(description, object);

  plMeshComponent* mesh;
  plMeshComponent::CreateComponent(object, mesh);
  mesh->SetMesh(sphere ? m_hBallMesh : m_hBoxMesh);
  mesh->SetColor(color);

  if (emissive)
    mesh->SetMaterial(0, plResourceManager::LoadResource<plMaterialResource>("Materials/Glow.plMaterial"));

  return handle;
}

void BrickBreakerGameState::BuildTable()
{
  m_hBoxMesh = CreateMesh("BrickBreaker.UnitBox", false);
  m_hBallMesh = CreateMesh("BrickBreaker.UnitSphere", true);

  plGameObjectDesc root;
  root.m_sName.Assign("Brick Breaker - generated review scene");
  m_hRoot = m_pMainWorld->CreateObject(root);

  AddMesh("Table base", plVec3(0, -0.5f, -0.6f), plVec3(19.4f, 22.4f, 1.0f), Ink);
  AddMesh("Playing surface", plVec3(0, -0.5f, -0.06f), plVec3(18.0f, 21.0f, 0.10f), plColor(0.028f, 0.055f, 0.085f));

  for (float x : {-9.25f, 9.25f})
  {
    AddMesh("Side rail", plVec3(x, -0.5f, 0.20f), plVec3(0.5f, 21.0f, 0.6f), plColor(0.10f, 0.16f, 0.23f));
    AddMesh("Rail light", plVec3(x, -0.5f, 0.51f), plVec3(0.08f, 21.0f, 0.04f), Mint, false, true);
  }

  AddMesh("Back rail", plVec3(0, 10.25f, 0.20f), plVec3(19.0f, 0.5f, 0.6f), plColor(0.10f, 0.16f, 0.23f));
  AddMesh("Back light", plVec3(0, 10.25f, 0.51f), plVec3(18.5f, 0.08f, 0.04f), Mint, false, true);

  AddMesh("Drain warning", plVec3(0, -10.6f, 0.01f), plVec3(17.6f, 0.07f, 0.02f), plColor(0.85f, 0.16f, 0.10f), false, true);

  for (int line = -8; line <= 8; line += 2)
    AddMesh("Surface inlay", plVec3(static_cast<float>(line), -0.5f, 0.005f), plVec3(0.018f, 20.5f, 0.008f), plColor(0.05f, 0.10f, 0.13f));

  m_hPaddle = AddMesh("Paddle", plVec3(0, Game::PaddleY, 0.34f), plVec3(Game::PaddleHalfWidth * 2, Game::PaddleHalfDepth * 2, 0.55f), Mint);
  m_hBall = AddMesh("Ball", plVec3(0, 0, 0.38f), plVec3(Game::BallRadius), plColor(1.0f, 0.88f, 0.60f), true, true);

  m_Bricks.Clear();
  for (plUInt32 i = 0; i < m_Game.m_Bricks.GetCount(); ++i)
  {
    const auto& brick = m_Game.m_Bricks[i];

    plStringBuilder name;
    name.SetFormat("Brick {}", i + 1);

    m_Bricks.PushBack(AddMesh(name, plVec3(brick.m_Position.x, brick.m_Position.y, 0.42f), plVec3(1.72f, 0.86f, 0.74f), RowColors[i / 8]));
  }

  plGameObjectDesc lightDescription;
  lightDescription.m_hParent = m_hRoot;
  lightDescription.m_sName.Assign("Table lighting");
  lightDescription.m_LocalRotation = plQuat::MakeShortestRotation(plVec3(1, 0, 0), plVec3(-0.3f, 0.4f, -1.0f).GetNormalized());

  plGameObject* lightObject;
  m_pMainWorld->CreateObject(lightDescription, lightObject);

  plDirectionalLightComponent* sun;
  plDirectionalLightComponent::CreateComponent(lightObject, sun);
  sun->SetIntensity(3.0f);
  sun->SetCastShadows(true);

  plAmbientLightComponent* ambient;
  plAmbientLightComponent::CreateComponent(lightObject, ambient);
  ambient->SetTopColor(plColor(0.30f, 0.38f, 0.50f));
  ambient->SetBottomColor(plColor(0.08f, 0.10f, 0.15f));
  ambient->SetIntensity(0.7f);
}

void BrickBreakerGameState::UpdateObjects()
{
  plGameObject* object;
  if (m_pMainWorld->TryGetObject(m_hPaddle, object))
    object->SetLocalPosition(plVec3(m_Game.m_fPaddleX, Game::PaddleY, 0.34f));

  if (m_pMainWorld->TryGetObject(m_hBall, object))
    object->SetLocalPosition(plVec3(m_Game.m_Ball.x, m_Game.m_Ball.y, 0.38f));

  for (plUInt32 i = 0; i < m_Bricks.GetCount(); ++i)
    if (m_pMainWorld->TryGetObject(m_Bricks[i], object))
      object->SetActiveFlag(m_Game.m_Bricks[i].m_bAlive);
}

void BrickBreakerGameState::CreateHud()
{
  m_pHudBoard = plBlackboard::Create();
  m_pHudBoard->SetName("BrickBreakerHUD");

  plGameObjectDesc description;
  description.m_sName.Assign("GameUI HUD");
  description.m_hParent = m_hRoot;

  plGameObject* object;
  m_pMainWorld->CreateObject(description, object);

  plUiCanvasComponent* canvas;
  plUiCanvasComponent::CreateComponent(object, canvas);
  canvas->SetUiDocument(plResourceManager::LoadResource<plUiDocumentResource>(
    "{ 444c6ad2-3011-5f64-93a0-723af8f77e29 }")); // UI/BrickBreakerHUD.plUiDocumentAsset
  canvas->SetReferenceHeight(900);
  canvas->SetScaleMode(plUiCanvasScaleMode::ScaleWithHeight);
  canvas->SetAutobindBlackboards(false);
  canvas->RegisterBlackboard(m_pHudBoard);
}

void BrickBreakerGameState::UpdateHud()
{
  // GameUI resolves these bindings only when values change. Layout and styling live in the UI document.
  m_pHudBoard->SetEntryValue(plMakeHashedString("Score"), m_Game.m_uiScore);
  m_pHudBoard->SetEntryValue(plMakeHashedString("Lives"), m_Game.m_uiLives);
  m_pHudBoard->SetEntryValue(plMakeHashedString("Bricks"), m_Game.RemainingBricks());

  const char* title = "";
  const char* subtitle = "";

  if (m_Game.m_bPaused)
  {
    title = "PAUSED";
    subtitle = "P or ESC to resume";
  }
  else if (m_Game.m_State == Game::State::Ready)
  {
    title = m_Game.m_uiLives == 3 ? "CLEAR THE TABLE" : "TRY ANOTHER BALL";
    subtitle = "Move into position, then press SPACE to launch";
  }
  else if (m_Game.m_State == Game::State::Won)
  {
    title = "TABLE CLEARED";
    subtitle = "24 bricks. Nicely done. Press R to play again.";
  }
  else if (m_Game.m_State == Game::State::Lost)
  {
    title = "OUT OF BALLS";
    subtitle = "Press R for a fresh table";
  }

  m_pHudBoard->SetEntryValue(plMakeHashedString("ShowPrompt"), title[0] != '\0');
  m_pHudBoard->SetEntryValue(plMakeHashedString("PromptTitle"), title);
  m_pHudBoard->SetEntryValue(plMakeHashedString("PromptSubtitle"), subtitle);
}
