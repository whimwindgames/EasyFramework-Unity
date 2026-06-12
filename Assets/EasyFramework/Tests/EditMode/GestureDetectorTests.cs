using System.Collections.Generic;
using EasyFramework.Services.Inputs;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class GestureDetectorTests
    {
        sealed class RecordingEmitter : IGestureEmitter
        {
            public readonly List<TapEvent> Taps = new();
            public readonly List<LongPressEvent> LongPresses = new();
            public readonly List<SwipeEvent> Swipes = new();
            public readonly List<DragEvent> Drags = new();
            public void Emit(TapEvent e) => Taps.Add(e);
            public void Emit(LongPressEvent e) => LongPresses.Add(e);
            public void Emit(SwipeEvent e) => Swipes.Add(e);
            public void Emit(DragEvent e) => Drags.Add(e);
        }

        static (GestureDetector d, RecordingEmitter e) Make()
        {
            var em = new RecordingEmitter();
            return (new GestureDetector(em), em);
        }

        [Test]
        public void QuickSmallMove_EmitsTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(105, 103), 0.1f); // <0.3s, 位移<20px
            Assert.AreEqual(1, e.Taps.Count);
            Assert.AreEqual(0, e.Swipes.Count);
        }

        [Test]
        public void SlowRelease_NotTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(102, 102), 0.4f); // >0.3s
            Assert.AreEqual(0, e.Taps.Count);
        }

        [Test]
        public void LargeMove_NotTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(130, 100), 0.1f); // 位移 30 >=20,但 <80 不算 swipe,且 >20 不算 tap
            Assert.AreEqual(0, e.Taps.Count);
        }

        [Test]
        public void HeldStill_EmitsLongPress()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(101, 101), 0.5f); // >=0.5s 静止
            Assert.AreEqual(1, e.LongPresses.Count);
        }

        [Test]
        public void LongPress_FiresOnce()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(100, 100), 0.6f);
            d.OnPointerMove(new Vector2(100, 100), 0.7f);
            Assert.AreEqual(1, e.LongPresses.Count, "长按只触发一次");
        }

        [Test]
        public void MoveBeyondThreshold_CancelsLongPress()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(130, 100), 0.2f); // 超 20px,转 Drag
            d.OnPointerMove(new Vector2(131, 100), 0.6f); // 即便过 0.5s 也不再 longpress
            Assert.AreEqual(0, e.LongPresses.Count);
            Assert.Greater(e.Drags.Count, 0);
        }

        [Test]
        public void FastLongMoveRight_EmitsSwipeRight()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(200, 110), 0.2f); // dx=100>=80, <0.5s, 主轴 X 正
            Assert.AreEqual(1, e.Swipes.Count);
            Assert.AreEqual(SwipeDirection.Right, e.Swipes[0].Direction);
        }

        [Test]
        public void FastLongMoveUp_EmitsSwipeUp()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(110, 200), 0.2f); // dy=100, 主轴 Y 正(屏幕坐标 Y 向上)
            Assert.AreEqual(SwipeDirection.Up, e.Swipes[0].Direction);
        }

        [Test]
        public void FastLongMoveLeftAndDown()
        {
            var (d1, e1) = Make();
            d1.OnPointerDown(new Vector2(200, 100), 0f);
            d1.OnPointerUp(new Vector2(100, 110), 0.2f);
            Assert.AreEqual(SwipeDirection.Left, e1.Swipes[0].Direction);

            var (d2, e2) = Make();
            d2.OnPointerDown(new Vector2(100, 200), 0f);
            d2.OnPointerUp(new Vector2(110, 100), 0.2f);
            Assert.AreEqual(SwipeDirection.Down, e2.Swipes[0].Direction);
        }

        [Test]
        public void SlowLongMove_NotSwipe_NoTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(200, 100), 0.6f); // 位移够但 >=0.5s,非 swipe;且 drag 已起,松手发 End
            Assert.AreEqual(0, e.Swipes.Count);
            Assert.AreEqual(0, e.Taps.Count);
        }

        [Test]
        public void Drag_EmitsStartMoveEnd()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(125, 100), 0.05f); // 超阈值,Start
            d.OnPointerMove(new Vector2(150, 100), 0.1f);  // Move
            d.OnPointerUp(new Vector2(150, 100), 0.15f);   // End
            Assert.AreEqual(DragPhase.Start, e.Drags[0].Phase);
            Assert.AreEqual(DragPhase.Move, e.Drags[1].Phase);
            Assert.AreEqual(DragPhase.End, e.Drags[^1].Phase);
        }

        [Test]
        public void Drag_DeltaIsBetweenConsecutivePositions()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(130, 100), 0.05f); // Start, delta 从按下点算
            d.OnPointerMove(new Vector2(140, 100), 0.1f);  // Move, delta = (10,0)
            var move = e.Drags[1];
            Assert.AreEqual(new Vector2(10, 0), move.Delta);
        }
    }
}
