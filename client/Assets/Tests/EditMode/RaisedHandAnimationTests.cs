using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CTLife.Tests
{
    public sealed class RaisedHandAnimationTests
    {
        [TestCase("Snow", AuthoredIdleImport.ClipPath, CastImport.SnowControllerPath)]
        [TestCase("Rain", RainIdleImport.ClipPath, CastImport.RainControllerPath)]
        public void StretchKeepsTheAuthoredBodyAndSuppliesEveryIndependentFinger(string family, string idlePath, string controllerPath)
        {
            var source = Clip(RaisedHandAnimationImport.SourcePath);
            var idle = Clip(idlePath);
            var repaired = AssetDatabase.LoadAssetAtPath<AnimationClip>(RaisedHandAnimationImport.OutputRoot + "/" + family + "_RelaxedHandsStretch.anim");
            Assert.IsNotNull(repaired, "Run RaisedHandAnimationImport.RunBatch before accepting this feature.");
            Assert.IsTrue(repaired.humanMotion);
            Assert.That(repaired.length, Is.EqualTo(source.length).Within(.0001f));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(controllerPath);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); controller.GetOverrides(pairs);
            var shared = AssetDatabase.LoadAssetAtPath<AnimatorController>(RaisedHandAnimationImport.SharedControllerPath);
            var sharedStretch = shared.layers.SelectMany(layer => layer.stateMachine.states).Single(child => child.state.name == "idle_fidget_stretch").state.motion as AnimationClip;
            Assert.That(sharedStretch.name, Is.EqualTo("Snow_RelaxedHandsStretch"));
            var pair = pairs.Single(item => item.Key == sharedStretch);
            Assert.That(pair.Value != null ? pair.Value : pair.Key, Is.SameAs(repaired));
            var digits = AnimationUtility.GetCurveBindings(idle).Where(RaisedHandAnimationImport.IsDigitCurve).ToArray();
            Assert.That(digits.Length, Is.EqualTo(40));
            foreach (var binding in digits)
            {
                var curve = AnimationUtility.GetEditorCurve(repaired, binding);
                Assert.IsNotNull(curve, binding.propertyName);
                float authored = AnimationUtility.GetEditorCurve(idle, binding).Evaluate(idle.length * .3f);
                for (int frame = 0; frame <= 90; frame++)
                    Assert.That(curve.Evaluate(source.length * frame / 90f), Is.EqualTo(authored).Within(.00001f), binding.propertyName);
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(source).Where(b => !RaisedHandAnimationImport.IsDigitCurve(b)))
            {
                var expected = AnimationUtility.GetEditorCurve(source, binding);
                var actual = AnimationUtility.GetEditorCurve(repaired, binding);
                Assert.IsNotNull(actual, binding.propertyName);
                for (int frame = 0; frame <= 90; frame++)
                    Assert.That(actual.Evaluate(source.length * frame / 90f), Is.EqualTo(expected.Evaluate(source.length * frame / 90f)).Within(.00001f),
                        "Only finger curves may change; " + binding.propertyName);
            }
        }

        private static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Single(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
    }
}
