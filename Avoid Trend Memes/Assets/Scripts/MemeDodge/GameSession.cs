using System;
using R3;

namespace MemeDodge
{
    public enum GamePhase { Title, Loading, Playing, Reward, GameOver, Victory }

    public sealed class GameSession : IDisposable
    {
        public const int MaxHealth = 5;
        public readonly ReactiveProperty<int> Health = new(MaxHealth);
        public readonly ReactiveProperty<int> ClearedStages = new(0);
        public readonly ReactiveProperty<int> Score = new(0);
        public readonly ReactiveProperty<GamePhase> Phase = new(GamePhase.Title);
        float invulnerableUntil;
        double scoreTenths;
        public bool PracticeMode { get; private set; }
        public bool DeveloperMode { get; set; }
        public int LostLives { get; private set; }

        public static string RankForScore(int score) => score >= 1000 ? "SS" : score >= 800 ? "S" : score >= 600 ? "A" : score >= 400 ? "B" : score >= 300 ? "C" : score >= 200 ? "D" : score >= 100 ? "E" : "F";

        public void AdvanceScore(float seconds)
        {
            if (Phase.Value != GamePhase.Playing || seconds <= 0) return;
            scoreTenths += (double)seconds * 10;
            int earned = (int)scoreTenths;
            if (earned == 0) return;
            scoreTenths -= earned;
            Score.Value += earned;
        }

        public void NewRun(bool practiceMode = false)
        {
            PracticeMode = practiceMode;
            LostLives = 0;
            Health.Value = MaxHealth;
            ClearedStages.Value = 0;
            Score.Value = 0;
            scoreTenths = 0;
            invulnerableUntil = float.NegativeInfinity;
            Phase.Value = GamePhase.Loading;
        }

        public bool Damage(float time, bool ignoreInvulnerability = false)
        {
            bool canDamage = Phase.Value == GamePhase.Playing || ignoreInvulnerability && Phase.Value == GamePhase.Reward;
            if (!canDamage || !ignoreInvulnerability && time < invulnerableUntil) return false;
            invulnerableUntil = time + 2f;
            LostLives++;
            Health.Value = Math.Max(0, Health.Value - 1);
            if (Health.Value == 0 && !PracticeMode && !DeveloperMode) Phase.Value = GamePhase.GameOver;
            return true;
        }

        public bool ClearStage()
        {
            if (Phase.Value != GamePhase.Playing) return false;
            ClearedStages.Value++;
            Score.Value += 100;
            Phase.Value = GamePhase.Reward;
            return true;
        }

        public bool CollectHealthPack()
        {
            if (Phase.Value != GamePhase.Reward) return false;
            Health.Value = Math.Min(MaxHealth, Health.Value + 1);
            Phase.Value = GamePhase.Playing;
            return true;
        }

        public void Dispose()
        {
            Health.Dispose();
            ClearedStages.Dispose();
            Score.Dispose();
            Phase.Dispose();
        }
    }
}
