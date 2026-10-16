using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// One piece of Game scene wiring in its OWN file (KAN-228). <see cref="GameSceneBuilder"/> finds every implementation and
    /// runs it after the core scene is built, before the save. New features add a step file instead of a line in
    /// GameSceneBuilder.Build, so parallel tickets do not conflict there. Needs a public parameterless constructor.
    /// </summary>
    public interface IGameSceneStep
    {
        /// <summary>Lower runs first; ties run in full type name order. Use the ticket number (e.g. 228) unless a step needs another first.</summary>
        int Order { get; }

        void Build(GameSceneContext ctx);
    }

    public static class GameSceneSteps
    {
        /// <summary>All steps in run order: Order, then full type name (stable between machines).</summary>
        public static IReadOnlyList<IGameSceneStep> All()
        {
            return TypeCache.GetTypesDerivedFrom<IGameSceneStep>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (IGameSceneStep)Activator.CreateInstance(t))
                .OrderBy(s => s.Order)
                .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal)
                .ToList();
        }

        public static void RunAll(GameSceneContext ctx)
        {
            foreach (var step in All())
            {
                step.Build(ctx);
                UnityEngine.Debug.Log($"[GameSceneBuilder] step {step.GetType().Name} (order {step.Order})");
            }
        }
    }
}
