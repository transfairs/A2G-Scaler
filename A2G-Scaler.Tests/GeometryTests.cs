using System.Drawing;
using Xunit;

namespace A2GScaler.Tests
{
    public class FitRectTests
    {
        static readonly Size Game = new Size(640, 480);

        [Fact]
        public void Smooth_FillsHeightAndCentres()
        {
            Assert.Equal(new Rectangle(240, 0, 1440, 1080), ScalerForm.FitRect(new Rectangle(0, 0, 1920, 1080), Game, false));
        }

        [Fact]
        public void Smooth_RespectsViewOffset()
        {
            Assert.Equal(new Rectangle(10, 30 + 15, 800, 600), ScalerForm.FitRect(new Rectangle(10, 30, 800, 630), Game, false));
        }

        [Fact]
        public void Smooth_NeverBelowOnePixel()
        {
            Assert.Equal(new Rectangle(0, 0, 1, 1), ScalerForm.FitRect(new Rectangle(0, 0, 0, 0), Game, false));
        }

        [Fact]
        public void Integer_UsesLargestWholeFactor()
        {
            Assert.Equal(new Rectangle(320, 60, 1280, 960), ScalerForm.FitRect(new Rectangle(0, 0, 1920, 1080), Game, true));
        }

        [Fact]
        public void Integer_AtLeastOnce_EvenIfTooSmall()
        {
            Assert.Equal(new Rectangle(-120, -90, 640, 480), ScalerForm.FitRect(new Rectangle(0, 0, 400, 300), Game, true));
        }
    }

    public class MouseMappingTests
    {
        static readonly Rectangle Src = new Rectangle(100, 100, 640, 480);
        static readonly Rectangle Dest = new Rectangle(0, 0, 1280, 960);

        [Fact]
        public void ToDest_ScalesFromGameOrigin()
        {
            Assert.Equal(new Point(0, 0), ScalerForm.ToDest(new Point(100, 100), Src, Dest));
            Assert.Equal(new Point(640, 480), ScalerForm.ToDest(new Point(420, 340), Src, Dest));
        }

        [Fact]
        public void ToSource_IsInverseOfToDest()
        {
            Assert.Equal(new Point(420, 340), ScalerForm.ToSource(new Point(640, 480), Src, Dest));
            Assert.Equal(new Point(420, 340), ScalerForm.ToSource(new Point(641, 481), Src, Dest)); // rounds down
        }

        [Fact]
        public void ToSource_ClampsIntoGameArea()
        {
            Assert.Equal(new Point(100, 100), ScalerForm.ToSource(new Point(-50, -50), Src, Dest));
            Assert.Equal(new Point(739, 579), ScalerForm.ToSource(new Point(5000, 5000), Src, Dest));
        }
    }

    public class PushTests
    {
        [Theory]
        [InlineData(10, true, 5, 15)]    // pushing outward at the edge builds up
        [InlineData(10, true, 0, 10)]    // moving along the edge keeps it
        [InlineData(10, true, -3, 0)]    // moving inward resets
        [InlineData(10, false, 5, 0)]    // away from the edge resets
        [InlineData(10, false, 0, 0)]
        public void Push(int pressure, bool atEdge, int outward, int expected)
        {
            Assert.Equal(expected, ScalerForm.Push(pressure, atEdge, outward));
        }
    }

    public class AttachPidTests
    {
        [Theory]
        [InlineData(new[] { "scaler.exe" }, 0)]
        [InlineData(new[] { "scaler.exe", "--attach", "1234" }, 1234)]
        [InlineData(new[] { "scaler.exe", "x", "--attach", "77" }, 77)]
        [InlineData(new[] { "scaler.exe", "--attach" }, 0)]            // no value
        [InlineData(new[] { "scaler.exe", "--attach", "abc" }, 0)]
        [InlineData(new[] { "scaler.exe", "--attach", "0" }, 0)]
        [InlineData(new[] { "scaler.exe", "--attach", "-5" }, 0)]
        [InlineData(new[] { "--attach", "1234" }, 0)]                    // args[0] is the program itself
        public void AttachPid(string[] args, int expected)
        {
            Assert.Equal(expected, Program.AttachPid(args));
        }
    }

    public class SharpScalerMathTests
    {
        [Fact]
        public void Weights_IntegerFactor_IsNearestNeighbour()
        {
            var idx = new int[4]; var wt = new int[4];
            SharpScaler.Weights(2, 4, idx, wt);
            Assert.Equal(new[] { 0, 0, 0, 0 }, idx);
            Assert.Equal(new[] { 0, 0, 256, 256 }, wt);
        }

        [Fact]
        public void Weights_FractionalFactor_BlendsAtTheSeam()
        {
            var idx = new int[3]; var wt = new int[3];
            SharpScaler.Weights(2, 3, idx, wt);
            Assert.Equal(new[] { 0, 0, 0 }, idx);
            Assert.Equal(new[] { 0, 128, 256 }, wt);
        }

        [Fact]
        public void Weights_StayInRange()
        {
            var idx = new int[1001]; var wt = new int[1001];
            SharpScaler.Weights(640, 1001, idx, wt);
            for (int o = 0; o < idx.Length; o++)
            {
                Assert.InRange(idx[o], 0, 638);
                Assert.InRange(wt[o], 0, 256);
            }
        }

        [Theory]
        [InlineData(0x123456, 0xABCDEF, 0, 0x123456)]
        [InlineData(0x123456, 0xABCDEF, 256, 0xABCDEF)]
        [InlineData(0x000000, 0x808080, 128, 0x404040)]
        [InlineData(0x00FF00, 0xFF0000, 128, 0x7F7F00)]
        public void Mix(int a, int b, int w, int expected)
        {
            Assert.Equal(expected, SharpScaler.Mix(a, b, w));
        }
    }
}
