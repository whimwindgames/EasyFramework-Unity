using System.Collections.Generic;
using EasyFramework.Services.Inputs;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class PinchDetectorTests
    {
        sealed class Sink
        {
            public readonly List<float> Scales = new();
            public void OnPinch(PinchEvent e) => Scales.Add(e.DeltaScale);
        }

        [Test]
        public void FirstFrame_OnlySetsBaseline_NoEmit()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(10, 0)); // 距离 10
            Assert.AreEqual(0, sink.Scales.Count);
        }

        [Test]
        public void Widening_EmitsScaleGreaterThanOne()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(10, 0));  // 基线 10
            p.Update(new Vector2(0, 0), new Vector2(20, 0));  // 20/10 = 2
            Assert.AreEqual(1, sink.Scales.Count);
            Assert.AreEqual(2f, sink.Scales[0], 1e-4f);
        }

        [Test]
        public void Narrowing_EmitsScaleLessThanOne()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(20, 0));  // 基线 20
            p.Update(new Vector2(0, 0), new Vector2(10, 0));  // 10/20 = 0.5
            Assert.AreEqual(0.5f, sink.Scales[0], 1e-4f);
        }

        [Test]
        public void Reset_RebuildsBaseline()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(10, 0));
            p.Reset();                                        // 松指
            p.Update(new Vector2(0, 0), new Vector2(30, 0));  // 新基线 30,不发
            Assert.AreEqual(0, sink.Scales.Count);
            p.Update(new Vector2(0, 0), new Vector2(60, 0));  // 60/30 = 2
            Assert.AreEqual(2f, sink.Scales[0], 1e-4f);
        }
    }
}
