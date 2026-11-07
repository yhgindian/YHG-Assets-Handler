using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Utils.Unity
{
    public class BundleCompressProgressBridge : IAssetBundleCompressProgress
    {
        private readonly Action<double, string> _reportCallback;
        private readonly double _startPct;
        private readonly double _weight;
        private readonly string _prefix;

        public BundleCompressProgressBridge(Action<double, string> reportCallback, double startPct, double weight, string prefix)
        {
            _reportCallback = reportCallback;
            _startPct = startPct;
            _weight = weight;
            _prefix = prefix;
        }

        public void SetProgress(float progress)
        {
            double currentPct = _startPct + (progress * _weight);
            _reportCallback(currentPct, $"{_prefix}: {(progress * 100):F0}%");
        }
    }
}
