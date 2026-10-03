using System;
using System.IO;
using MemeDodge;
using R3;
using UnityEditor;
using UnityEngine;

public static class ValidateGameRules
{
    [MenuItem("Tools/Avoid Trend Memes/Validate Game Rules")]
    public static void Validate()
    {
        using var session = new GameSession();
        int observedHealth = -1;
        using var subscription = session.Health.Subscribe(value => observedHealth = value);
        Require(session.Health.Value == 5 && observedHealth == 5, "Starting HP and reactive UI value");
        Require(!session.Damage(0), "No damage on title screen");
        session.AdvanceScore(2);
        Require(session.Score.Value == 0, "Title does not earn points");
        session.NewRun();
        session.AdvanceScore(2);
        Require(session.Score.Value == 0, "Loading does not earn points");
        session.Phase.Value = GamePhase.Playing;
        session.AdvanceScore(.05f);
        Require(session.Score.Value == 0, "Fractional second is held");
        session.AdvanceScore(.26f);
        Require(session.Score.Value == 3, "One point per complete tenth of a second");
        Require(session.Damage(0) && observedHealth == 4, "Damage updates health observers");
        Require(!session.Damage(.5f) && session.Health.Value == 4, "One-second invulnerability");
        Require(session.ClearStage() && session.ClearedStages.Value == 1, "Stage clear enters reward phase");
        Require(session.Score.Value == 103, "Stage clear grants 100 points");
        session.AdvanceScore(5);
        Require(session.Score.Value == 103, "Reward waiting does not earn points");
        Require(!session.ClearStage() && !session.Damage(2), "No repeated rewards or damage during reward");
        Require(session.Score.Value == 103, "Clear bonus cannot be duplicated");
        Require(session.CollectHealthPack() && observedHealth == 5, "Health pack restores one HP");
        Require(session.Phase.Value == GamePhase.Playing, "Pickup resumes playing without a loading pause");
        Require(!session.CollectHealthPack(), "Health pack can only be collected once");
        session.Phase.Value = GamePhase.Playing;
        session.ClearStage(); session.CollectHealthPack();
        Require(session.Health.Value == 5, "Healing is capped at five");
        session.Phase.Value = GamePhase.Playing;
        for (int i = 0; i < 5; i++) Require(session.Damage(10 + i * 2), "Damage while vulnerable");
        Require(session.Health.Value == 0 && session.Phase.Value == GamePhase.GameOver, "Zero HP enters game over");
        session.NewRun();
        Require(session.Score.Value == 0, "Restart resets points");
        int[] boundaries = { 0, 100, 200, 300, 400, 600, 800, 1000 };
        string[] ranks = { "F", "E", "D", "C", "B", "A", "S", "SS" };
        for(int i=0;i<boundaries.Length;i++)
        {
            Require(GameSession.RankForScore(boundaries[i]) == ranks[i], "Rank threshold");
            if(i>0) Require(GameSession.RankForScore(boundaries[i]-1) == ranks[i-1], "Below rank threshold");
        }
        Require(session.Health.Value == 5 && session.ClearedStages.Value == 0 && session.Phase.Value == GamePhase.Loading, "Restart resets run state");
        Directory.CreateDirectory("../tmp/verification");
        File.WriteAllText("../tmp/verification/game-rules.txt", "PASS: HP, invulnerability, clear, pickup, game over, restart, score timing, clear bonus, score reset, all rank boundaries\n" + DateTime.UtcNow.ToString("O"));
        Debug.Log("Avoid Trend Memes game rule checks passed.");
    }
    static void Require(bool condition, string description) { if (!condition) throw new Exception("Rule check failed: " + description); }
}
