using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 노트 연출(눈송이 흔들림 / 낙하 속도 카오스)은 "그려지는 위치"만 건드린다.
        //
        // 판정에 영향이 없는 이유: 판정은 JudgementUnit.Judge(NoteObjectBase)가
        // 노트의 Timing(= PrimitiveNote.Timing, 차트 데이터)과 재생 시각을 AcceptableTiming으로
        // 비교해서 낸다. 노트 오브젝트의 RectTransform은 어디에도 들어가지 않는다.
        // 그래서 여기서 노트를 아무리 옮겨도 판정·점수·기록은 그대로다.
        //
        // 훅 지점: StargazerNote.Behaviour(Single) / StargazerLongNote.Behaviour(Single)
        //   게임이 매 프레임 노트마다 부르는 위치 갱신 함수. 인자 deltatime은 "판정선까지 남은 초"다
        //   (NoteObjectBase.OnUpdate가 Timing - 현재시각을 넘긴다). 판정선을 지나면 음수가 된다.
        //
        // Prefix에서 우리가 지난 프레임에 얹은 오프셋을 되돌려 게임에 원래 위치를 돌려주고,
        // Postfix에서 게임이 새로 계산한 위치를 기준값으로 삼아 다시 얹는다.
        // 이렇게 하면 게임이 위치를 절대값으로 쓰든 이전 값에 누적하든 오프셋이 쌓이지 않는다.
        private static readonly Dictionary<IntPtr, NoteMotionState> NoteMotionStates = new Dictionary<IntPtr, NoteMotionState>();

        // 레인 단위로 공유하는 값(진행 방향, 레인 모드일 때의 속도 배율).
        // 키는 노트의 부모(레인 노트 컨테이너) Transform의 InstanceID.
        private static readonly Dictionary<int, NoteMotionLane> NoteMotionLanes = new Dictionary<int, NoteMotionLane>();
        private static readonly System.Random NoteMotionRandom = new System.Random();

        private static int NoteMotionLastPruneFrame;
        private static int NoteMotionCachedFrame = -1;
        private static float NoteMotionCachedTime;
        private static bool NoteMotionVelocityLogged;

        private static bool NoteMotionEnabled => CustomConfig.NoteSway || CustomConfig.NoteSpeedChaos;

        // 곡을 새로 시작할 때마다 비운다. 노트 오브젝트가 전부 새로 만들어지고,
        // 해제된 IL2CPP 주소가 재사용되면 엉뚱한 노트의 상태를 물려받기 때문이다.
        private static void ResetNoteMotionState()
        {
            NoteMotionStates.Clear();
            NoteMotionLanes.Clear();
            NoteMotionVelocityLogged = false;
        }

        private static void RestoreNoteBasePosition(object? instance)
        {
            if (!NoteMotionEnabled)
            {
                return;
            }

            try
            {
                if (instance is not Il2CppObjectBase note)
                {
                    return;
                }

                if (!NoteMotionStates.TryGetValue(note.Pointer, out NoteMotionState? state)
                    || !state.HasBase
                    || state.LastFrame == Time.frameCount)
                {
                    return;
                }

                RectTransform? transform = TryGetNoteRectTransform(note);
                if (transform == null)
                {
                    return;
                }

                transform.anchoredPosition = state.BasePosition;
            }
            catch (Exception ex)
            {
                WarnNoteMotionOnce("prefix", ex);
            }
        }

        private static void ApplyNoteMotion(object? instance, float deltatime)
        {
            if (!NoteMotionEnabled)
            {
                return;
            }

            try
            {
                if (instance is not Il2CppObjectBase note)
                {
                    return;
                }

                RectTransform? transform = TryGetNoteRectTransform(note);
                if (transform == null)
                {
                    return;
                }

                int frame = Time.frameCount;
                IntPtr pointer = note.Pointer;
                Vector2 basePosition = transform.anchoredPosition;

                // 한동안 갱신되지 않았던 항목은 해제된 주소를 재사용한 새 노트일 수 있어 새로 만든다.
                // (일시정지 후 복귀에도 걸리는데, 그때는 위상과 배율이 새로 뽑혀 한 번 튄다.)
                bool isNewNote = !NoteMotionStates.TryGetValue(pointer, out NoteMotionState? known) || frame - known.LastFrame > 5;
                if (!isNewNote && known!.LastFrame == frame)
                {
                    // 롱노트는 override가 base.Behaviour를 부르므로 한 프레임에 두 번 들어온다.
                    return;
                }

                NoteMotionState state;
                if (isNewNote)
                {
                    state = CreateNoteMotionState(transform);
                    NoteMotionStates[pointer] = state;
                }
                else
                {
                    state = known!;
                    UpdateNoteVelocity(state, basePosition, deltatime);
                }

                state.BasePosition = basePosition;
                state.LastDeltatime = deltatime;
                state.HasBase = true;
                state.LastFrame = frame;

                float offsetX = 0f;
                float offsetY = 0f;

                // 남은 시간 t에서의 위치는 P(t) = 판정선 + v·t 이므로, 배율 f를 먹인 위치는
                // P(t·f) = P(t) + v·t·(f-1). 판정선 위치를 알 필요 없이 오프셋만으로 계산된다.
                // t가 0으로 갈수록 오프셋도 0이라 어떤 배율이든 판정선에는 정확히 제때 도착한다.
                if (state.ChaosFactor != 1f && state.HasVelocity)
                {
                    float scale = deltatime * (state.ChaosFactor - 1f);
                    offsetX = state.VelocityX * scale;
                    offsetY = state.VelocityY * scale;
                }

                if (CustomConfig.NoteSway)
                {
                    float sway = ComputeNoteSwayOffset(state.SwayPhase, deltatime, frame);
                    if (state.SwayOnX)
                    {
                        offsetX += sway;
                    }
                    else
                    {
                        offsetY += sway;
                    }
                }

                if (offsetX != 0f || offsetY != 0f)
                {
                    Vector2 result = basePosition;
                    result.x += offsetX;
                    result.y += offsetY;
                    transform.anchoredPosition = result;
                }

                PruneNoteMotionStates(frame);
            }
            catch (Exception ex)
            {
                WarnNoteMotionOnce("postfix", ex);
            }
        }

        // 노트가 어느 방향으로 얼마나 빨리 가는지를 게임에 묻지 않고 직접 잰다.
        // 두 프레임의 (위치, 남은 시간)만 있으면 v = Δ위치 / Δ남은시간으로 나온다.
        //
        // StargazerNote.posRef를 판정선 기준점으로 쓰려던 첫 구현은 실패했다. 실측 결과
        // posRef는 노트의 현재 위치와 값이 같아서(2026-08-09: pos=(0,1435.7) posRef=(0,1435.7))
        // 기준점 역할을 하지 못한다.
        private static void UpdateNoteVelocity(NoteMotionState state, Vector2 basePosition, float deltatime)
        {
            if (!state.HasBase)
            {
                return;
            }

            float elapsed = deltatime - state.LastDeltatime;
            if (MathF.Abs(elapsed) < 0.0001f)
            {
                return;
            }

            float velocityX = (basePosition.x - state.BasePosition.x) / elapsed;
            float velocityY = (basePosition.y - state.BasePosition.y) / elapsed;

            if (state.HasVelocity)
            {
                // 프레임 간 미세한 오차로 노트가 떨리지 않도록 완만하게 따라간다.
                const float FollowRate = 0.25f;
                state.VelocityX += (velocityX - state.VelocityX) * FollowRate;
                state.VelocityY += (velocityY - state.VelocityY) * FollowRate;
            }
            else
            {
                state.VelocityX = velocityX;
                state.VelocityY = velocityY;
                state.HasVelocity = true;

                // 진행 축과 직각으로 흔들어야 "옆으로 흔들린다"가 된다.
                // 세로로 내려오는 노트면 X, 가로로 흐르는 노트면 Y.
                state.SwayOnX = MathF.Abs(velocityY) >= MathF.Abs(velocityX);
                LogNoteMotionVelocityOnce(state, basePosition, deltatime);
            }

            StoreLaneMotion(state);
        }

        private static NoteMotionState CreateNoteMotionState(RectTransform transform)
        {
            int laneKey = TryGetLaneKey(transform);
            var state = new NoteMotionState
            {
                LaneKey = laneKey,
                SwayPhase = (float)(NoteMotionRandom.NextDouble() * Math.PI * 2d),
                ChaosFactor = PickNoteChaosFactor(laneKey),
            };

            // 같은 레인의 다른 노트가 이미 진행 방향을 재 뒀으면 첫 프레임부터 그대로 쓴다.
            // 그러지 않으면 노트마다 두 번째 프레임에 오프셋이 한꺼번에 붙어 눈에 띄게 튄다.
            if (laneKey != 0 && NoteMotionLanes.TryGetValue(laneKey, out NoteMotionLane? lane) && lane.HasVelocity)
            {
                state.VelocityX = lane.VelocityX;
                state.VelocityY = lane.VelocityY;
                state.HasVelocity = true;
                state.SwayOnX = lane.SwayOnX;
            }

            return state;
        }

        private static void StoreLaneMotion(NoteMotionState state)
        {
            if (state.LaneKey == 0)
            {
                return;
            }

            NoteMotionLane lane = GetOrCreateLane(state.LaneKey);
            lane.VelocityX = state.VelocityX;
            lane.VelocityY = state.VelocityY;
            lane.HasVelocity = true;
            lane.SwayOnX = state.SwayOnX;
        }

        private static NoteMotionLane GetOrCreateLane(int laneKey)
        {
            if (!NoteMotionLanes.TryGetValue(laneKey, out NoteMotionLane? lane))
            {
                lane = new NoteMotionLane();
                NoteMotionLanes[laneKey] = lane;
            }

            return lane;
        }

        private static int TryGetLaneKey(RectTransform transform)
        {
            Transform? parent = transform.parent;
            return parent == null ? 0 : parent.GetInstanceID();
        }

        private static float PickNoteChaosFactor(int laneKey)
        {
            if (!CustomConfig.NoteSpeedChaos)
            {
                return 1f;
            }

            if (!CustomConfig.NoteSpeedChaosPerLane || laneKey == 0)
            {
                return RandomChaosFactor();
            }

            // 같은 레인 = 같은 부모 아래. 레인마다 배율을 하나만 뽑으면 레인 안에서는 순서가 유지된다.
            NoteMotionLane lane = GetOrCreateLane(laneKey);
            if (!lane.HasChaosFactor)
            {
                lane.ChaosFactor = RandomChaosFactor();
                lane.HasChaosFactor = true;
            }

            return lane.ChaosFactor;
        }

        private static float RandomChaosFactor()
        {
            float min = CustomConfig.NoteSpeedChaosMin;
            float max = CustomConfig.NoteSpeedChaosMax;
            return min + ((float)NoteMotionRandom.NextDouble() * (max - min));
        }

        private static float ComputeNoteSwayOffset(float phase, float deltatime, int frame)
        {
            float amplitude = CustomConfig.NoteSwayAmplitude;
            if (amplitude <= 0f)
            {
                return 0f;
            }

            float wave = MathF.Sin((NoteMotionTime(frame) * CustomConfig.NoteSwaySpeed * 2f * MathF.PI) + phase);
            if (!CustomConfig.NoteSwayDamping)
            {
                return amplitude * wave;
            }

            // deltatime = 판정선까지 남은 시간. 0에 가까워질수록 흔들림을 0으로 줄인다.
            float dampingTime = CustomConfig.NoteSwayDampingTime;
            if (dampingTime <= 0f)
            {
                return amplitude * wave;
            }

            float damping = deltatime / dampingTime;
            damping = damping < 0f ? 0f : (damping > 1f ? 1f : damping);
            return amplitude * wave * damping;
        }

        private static float NoteMotionTime(int frame)
        {
            if (NoteMotionCachedFrame != frame)
            {
                NoteMotionCachedFrame = frame;
                NoteMotionCachedTime = Time.time;
            }

            return NoteMotionCachedTime;
        }

        private static RectTransform? TryGetNoteRectTransform(Il2CppObjectBase note)
        {
            Component? component = note.TryCast<Component>();
            if (component == null)
            {
                return null;
            }

            Transform? transform = component.transform;
            return transform == null ? null : transform.TryCast<RectTransform>();
        }

        private static void PruneNoteMotionStates(int frame)
        {
            if (frame - NoteMotionLastPruneFrame < 600)
            {
                return;
            }

            NoteMotionLastPruneFrame = frame;

            var stale = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, NoteMotionState> pair in NoteMotionStates)
            {
                if (frame - pair.Value.LastFrame > 120)
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (IntPtr key in stale)
            {
                NoteMotionStates.Remove(key);
            }
        }

        // 진행 방향을 제대로 쟀는지 로그로 확인할 수 있게 곡마다 한 번만 찍는다.
        private static void LogNoteMotionVelocityOnce(NoteMotionState state, Vector2 basePosition, float deltatime)
        {
            if (NoteMotionVelocityLogged)
            {
                return;
            }

            NoteMotionVelocityLogged = true;
            MelonLogger.Msg(
                $"[NoteMotion] 진행 방향 측정: pos=({basePosition.x:0.#},{basePosition.y:0.#}) deltatime={deltatime:0.###}s "
                + $"v=({state.VelocityX:0.#},{state.VelocityY:0.#})px/s swayAxis={(state.SwayOnX ? "X" : "Y")} "
                + $"chaos={state.ChaosFactor:0.##}");
        }

        private static void WarnNoteMotionOnce(string phase, Exception ex)
        {
            if (LogOnce($"NoteMotion.{phase}.{ex.GetType().Name}"))
            {
                MelonLogger.Warning($"[NoteMotion][{phase}] 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private sealed class NoteMotionState
        {
            public Vector2 BasePosition;
            public bool HasBase;
            public int LastFrame = -1;
            public float LastDeltatime;

            public float VelocityX;
            public float VelocityY;
            public bool HasVelocity;

            public int LaneKey;
            public float SwayPhase;
            public bool SwayOnX = true;
            public float ChaosFactor = 1f;
        }

        private sealed class NoteMotionLane
        {
            public float VelocityX;
            public float VelocityY;
            public bool HasVelocity;
            public bool SwayOnX = true;
            public float ChaosFactor = 1f;
            public bool HasChaosFactor;
        }
    }
}
