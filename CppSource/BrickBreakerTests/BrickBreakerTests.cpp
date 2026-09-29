#include <BrickBreakerPlugin/Gameplay/BrickBreakerSimulation.h>
#include <cstdio>

namespace
{
  int s_iFailures = 0;

  void Check(bool condition, const char* description)
  {
    std::printf("%s: %s\n", condition ? "PASS" : "FAIL", description);
    s_iFailures += condition ? 0 : 1;
  }
} // namespace

int main()
{
  using Game = plBrickBreakerSimulation;

  Game game;

  Check(game.RemainingBricks() == 24 && game.m_uiLives == 3 && game.m_uiScore == 0, "Fresh table has 24 bricks, three lives, zero score");

  for (int i = 0; i < 600; ++i)
    game.Step(1);

  Check(plMath::IsEqual(game.m_fPaddleX, Game::HalfWidth - Game::PaddleHalfWidth, 0.001f) &&
          game.m_Ball.x == game.m_fPaddleX && game.m_State == Game::State::Ready,
    "Paddle stays inside rails and docked ball follows it");

  game.m_bPaused = true;
  const auto position = game.m_Ball;

  game.Launch();
  game.Step(-1);

  Check(game.m_Ball == position && game.m_State == Game::State::Ready, "Pause blocks movement and launch");

  game.m_bPaused = false;
  game.Launch();

  Check(game.m_State == Game::State::Playing && game.m_Velocity.y > 0, "Launch starts an upward flight");

  game.Reset();
  game.Launch();
  game.m_Ball.Set(Game::HalfWidth - Game::BallRadius - 0.01f, 0);
  game.m_Velocity.Set(10, 5);

  game.Step(0);

  Check(game.m_Velocity.x < 0 && game.m_Ball.x <= Game::HalfWidth - Game::BallRadius, "Side rail reflects and contains the ball");

  for (float contact : {-1.0f, 0.0f, 1.0f})
  {
    game.Reset();
    game.Launch();
    game.m_Ball.Set(contact, Game::PaddleY + Game::PaddleHalfDepth + Game::BallRadius + 0.02f);
    game.m_Velocity.Set(0, -11);

    game.Step(0);

    Check(game.m_Velocity.y > 0 && plMath::IsEqual(game.m_Velocity.GetLength(), game.m_fBallSpeed, 0.001f) &&
            (contact == 0 ? plMath::Abs(game.m_Velocity.x) < 0.001f : contact * game.m_Velocity.x > 0),
      "Paddle contact position controls rebound direction without changing speed");
  }

  game.Reset();
  game.Launch();
  game.m_Ball.Set(-7, 1.8f);
  game.m_Velocity.Set(0, 180); // Deliberately beyond shipped speed to exercise tunnelling protection.

  game.Step(0);

  Check(game.m_uiScore == 100 && game.RemainingBricks() == 23 && game.m_Velocity.y < 0,
    "Fast ball hits a brick once instead of tunnelling through it");

  for (int i = 0; i < 8; ++i)
    game.Step(0);

  Check(game.m_uiScore == 100, "Destroyed brick cannot score twice");

  game.Reset();
  for (auto& brick : game.m_Bricks)
    brick.m_bAlive = false;

  game.m_Bricks[0].m_bAlive = true;
  game.Launch();
  game.m_Ball.Set(-7, 2.2f);
  game.m_Velocity.Set(0, 18);

  game.Step(0);

  Check(game.m_State == Game::State::Won && game.RemainingBricks() == 0, "Last brick ends the round in victory");

  const auto wonPosition = game.m_Ball;
  game.Step(1);

  Check(game.m_Ball == wonPosition, "Won round stops simulation");

  game.Reset();
  for (plUInt32 remainingLives = 2;; --remainingLives)
  {
    game.Launch();
    game.m_Ball.Set(7, Game::Drain + 0.01f);
    game.m_Velocity.Set(0, -11);

    game.Step(0);

    Check(game.m_uiLives == remainingLives && game.m_State == (remainingLives == 0 ? Game::State::Lost : Game::State::Ready),
      "Drain consumes exactly one life and selects ready/game-over correctly");

    if (remainingLives == 0)
      break;
  }

  game.Launch();
  game.Step(0);

  Check(game.m_uiLives == 0 && game.m_State == Game::State::Lost, "Game-over cannot relaunch or underflow lives");

  game.m_bPaused = true;
  game.Reset();

  Check(game.RemainingBricks() == 24 && game.m_uiLives == 3 && game.m_uiScore == 0 && !game.m_bPaused &&
          game.m_fBallSpeed == Game::InitialBallSpeed && game.m_State == Game::State::Ready,
    "Restart restores the entire game state");

  float time;
  plVec2 normal;

  Check(!Game::SweepBox(plVec2(4, 0), plVec2(0, 100), plVec2(0, 3), plVec2(1, 1), 1, time, normal),
    "Parallel sweep outside a brick is not a hit");

  // A reproducible controller plays a complete run through the actual rules.
  game.Reset();
  for (int tick = 0; tick < 120 * 240 && game.m_State != Game::State::Won && game.m_State != Game::State::Lost; ++tick)
  {
    game.Launch();

    const float target = game.m_Ball.x + plMath::Sin(plAngle::MakeFromDegree(static_cast<float>(tick) * 0.37f)) * 1.1f;
    const float input = plMath::Clamp((target - game.m_fPaddleX) / (Game::PaddleSpeed * Game::FixedStep), -1.0f, 1.0f);
    game.Step(input);

    if (!game.m_Ball.IsValid() || !game.m_Velocity.IsValid())
      break;
  }

  std::printf("Full run: %u bricks, %u lives, %u points\n", game.RemainingBricks(), game.m_uiLives, game.m_uiScore);

  Check(game.m_Ball.IsValid() && game.m_State == Game::State::Won && game.m_uiScore == 2400,
    "Reproducible full run clears all 24 bricks with finite motion and 2400 points");

  std::printf("%d failure(s)\n", s_iFailures);
  return s_iFailures == 0 ? 0 : 1;
}
