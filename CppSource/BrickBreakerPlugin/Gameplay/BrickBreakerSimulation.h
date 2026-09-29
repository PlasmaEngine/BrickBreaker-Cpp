#pragma once

#include <Foundation/Containers/StaticArray.h>
#include <Foundation/Math/Vec2.h>

// The game lives on a horizontal plane in the 3D scene. Swept bounds keep the
// arcade ball predictable without a dependency on a rigid-body plugin.
struct plBrickBreakerSimulation
{
  enum class State
  {
    Ready,
    Playing,
    Won,
    Lost
  };

  struct Brick
  {
    plVec2 m_Position;
    bool m_bAlive = true;
  };

  static constexpr float HalfWidth = 9.0f;
  static constexpr float BackWall = 10.0f;
  static constexpr float Drain = -11.0f;

  static constexpr float PaddleY = -8.2f;
  static constexpr float PaddleHalfWidth = 1.65f;
  static constexpr float PaddleHalfDepth = 0.35f;

  static constexpr float BallRadius = 0.28f;
  static constexpr float PaddleSpeed = 16.0f;
  static constexpr float InitialBallSpeed = 11.0f;
  static constexpr float MaximumBallSpeed = 18.0f;

  static constexpr float FixedStep = 1.0f / 120.0f;
  static constexpr plUInt32 BrickCount = 24;

  State m_State = State::Ready;
  bool m_bPaused = false;
  plUInt32 m_uiLives = 3;
  plUInt32 m_uiScore = 0;

  float m_fPaddleX = 0;
  float m_fBallSpeed = InitialBallSpeed;
  plVec2 m_Ball = plVec2::MakeZero();
  plVec2 m_Velocity = plVec2::MakeZero();

  plStaticArray<Brick, BrickCount> m_Bricks;

  plBrickBreakerSimulation() { Reset(); }

  void Reset()
  {
    m_State = State::Ready;
    m_bPaused = false;
    m_uiLives = 3;
    m_uiScore = 0;

    m_fPaddleX = 0;
    m_fBallSpeed = InitialBallSpeed;

    m_Bricks.Clear();
    for (plUInt32 row = 0; row < 3; ++row)
      for (plUInt32 column = 0; column < 8; ++column)
        m_Bricks.PushBack({plVec2(-7.0f + column * 2.0f, 3.0f + row * 1.6f), true});

    DockBall();
  }

  void DockBall()
  {
    m_Ball.Set(m_fPaddleX, PaddleY + PaddleHalfDepth + BallRadius + 0.04f);
    m_Velocity.SetZero();
  }

  void Launch()
  {
    if (m_State != State::Ready || m_bPaused)
      return;

    m_State = State::Playing;
    m_Velocity = plVec2(0.23f, 1.0f).GetNormalized() * m_fBallSpeed;
  }

  plUInt32 RemainingBricks() const
  {
    plUInt32 count = 0;

    for (const auto& brick : m_Bricks)
      count += brick.m_bAlive ? 1 : 0;

    return count;
  }

  // Sweep a point against a box expanded by the ball radius. Returns time in
  // seconds, not a frame fraction; no collision can be skipped at high speed.
  static bool SweepBox(const plVec2& origin, const plVec2& velocity, const plVec2& center,
    const plVec2& halfSize, float limit, float& out_time, plVec2& out_normal)
  {
    float enter = -plMath::Infinity<float>();
    float leave = limit;
    plVec2 normal = plVec2::MakeZero();

    for (plUInt32 axis = 0; axis < 2; ++axis)
    {
      const float low = center.GetData()[axis] - halfSize.GetData()[axis] - BallRadius;
      const float high = center.GetData()[axis] + halfSize.GetData()[axis] + BallRadius;

      if (plMath::Abs(velocity.GetData()[axis]) < 0.00001f)
      {
        if (origin.GetData()[axis] < low || origin.GetData()[axis] > high)
          return false;

        continue;
      }

      float nearTime = (low - origin.GetData()[axis]) / velocity.GetData()[axis];
      float farTime = (high - origin.GetData()[axis]) / velocity.GetData()[axis];
      const float sign = velocity.GetData()[axis] > 0 ? -1.0f : 1.0f;

      if (nearTime > farTime)
        plMath::Swap(nearTime, farTime);

      if (nearTime > enter)
      {
        enter = nearTime;
        normal.SetZero();
        normal.GetData()[axis] = sign;
      }

      leave = plMath::Min(leave, farTime);
      if (enter > leave)
        return false;
    }

    if (enter < -0.00001f || enter > limit)
      return false;

    out_time = plMath::Max(0.0f, enter);
    out_normal = normal;

    return true;
  }

  void Step(float input)
  {
    if (m_bPaused || m_State == State::Won || m_State == State::Lost)
      return;

    m_fPaddleX = plMath::Clamp(m_fPaddleX + plMath::Clamp(input, -1.0f, 1.0f) * PaddleSpeed * FixedStep,
      -HalfWidth + PaddleHalfWidth, HalfWidth - PaddleHalfWidth);

    if (m_State == State::Ready)
    {
      DockBall();
      return;
    }

    float remaining = FixedStep;
    for (plUInt32 bounce = 0; bounce < 8 && remaining > 0.000001f; ++bounce)
    {
      float hitTime = remaining;
      plVec2 normal = plVec2::MakeZero();
      int hit = -1; // -1: none, -2: wall, -3: paddle, -4: drain; otherwise brick index

      auto wall = [&](float time, plVec2 wallNormal, int kind) {
        if (time >= 0 && time <= hitTime)
        {
          hitTime = time;
          normal = wallNormal;
          hit = kind;
        }
      };

      if (m_Velocity.x < 0)
        wall((-HalfWidth + BallRadius - m_Ball.x) / m_Velocity.x, plVec2(1, 0), -2);

      if (m_Velocity.x > 0)
        wall((HalfWidth - BallRadius - m_Ball.x) / m_Velocity.x, plVec2(-1, 0), -2);

      if (m_Velocity.y > 0)
        wall((BackWall - BallRadius - m_Ball.y) / m_Velocity.y, plVec2(0, -1), -2);

      if (m_Velocity.y < 0)
      {
        wall((Drain - m_Ball.y) / m_Velocity.y, plVec2(0, 1), -4);

        // Only the top face catches the ball. A paddle moved sideways into a
        // descending ball cannot repeatedly reflect it underneath the paddle.
        const float time = (PaddleY + PaddleHalfDepth + BallRadius - m_Ball.y) / m_Velocity.y;
        const float x = m_Ball.x + m_Velocity.x * time;

        if (plMath::Abs(x - m_fPaddleX) <= PaddleHalfWidth + BallRadius)
          wall(time, plVec2(0, 1), -3);
      }

      for (plUInt32 i = 0; i < m_Bricks.GetCount(); ++i)
      {
        float time;
        plVec2 n;

        if (m_Bricks[i].m_bAlive && SweepBox(m_Ball, m_Velocity, m_Bricks[i].m_Position, plVec2(0.86f, 0.43f), hitTime, time, n))
        {
          hitTime = time;
          normal = n;
          hit = static_cast<int>(i);
        }
      }

      m_Ball += m_Velocity * hitTime;
      remaining -= hitTime;

      if (hit == -1)
        break;

      if (hit == -4)
      {
        --m_uiLives;
        m_State = m_uiLives == 0 ? State::Lost : State::Ready;
        m_fBallSpeed = InitialBallSpeed;

        DockBall();
        return;
      }

      if (hit >= 0)
      {
        m_Bricks[hit].m_bAlive = false;
        m_uiScore += 100;
        m_fBallSpeed = plMath::Min(MaximumBallSpeed, m_fBallSpeed + 0.22f);

        if (RemainingBricks() == 0)
        {
          m_State = State::Won;
          m_Velocity.SetZero();
          return;
        }
      }

      if (hit == -3)
      {
        const float offset = plMath::Clamp((m_Ball.x - m_fPaddleX) / PaddleHalfWidth, -1.0f, 1.0f);

        // Aim by contact position; a central hit still makes forward progress.
        const float horizontal = offset * 0.85f;
        m_Velocity = plVec2(horizontal, plMath::Sqrt(1.0f - horizontal * horizontal)) * m_fBallSpeed;
      }
      else
      {
        m_Velocity -= normal * (2.0f * m_Velocity.Dot(normal));
        m_Velocity = m_Velocity.GetNormalized() * m_fBallSpeed;
      }

      m_Ball += normal * 0.001f;
    }
  }
};
