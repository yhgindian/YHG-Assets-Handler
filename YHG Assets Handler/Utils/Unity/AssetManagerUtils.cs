using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Reflection;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class AssetManagerUtils
    {
        public static AssetsManager Init()
        {
            var am = new AssetsManager();
            am.LoadClassPackage("YHGIndian/classdata.tpk");
            return am;
        }

        public static AssetTypeTemplateField CreateTemplateField(
            this AssetsManager am, int classid)
        {
            return am.ClassDatabase.CreateTemplateField(classid);
        }

        public static AssetTypeValueField CreateEmptyBaseField(
            this AssetsManager am, int classid)
        {
            return am.ClassDatabase.CreateEmptyBaseField(classid);
        }

        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo TemplateFieldCache =
            GetPrivateField("templateFieldCache");

        private static FieldInfo GetPrivateField(string name)
        {
            return typeof(AssetsManager).GetField(name, PrivateInstance)!;
        }

        /// <summary>
        /// Clears cached MonoBehaviour templates after assigning a
        /// MonoBehaviour template generator.
        /// </summary>
        public static void ClearMonoTemplateCache(this AssetsManager am)
        {
            Status.Write("[MonoCache] Attempting to clear MonoBehaviour template cache...");
            var cacheObj = TemplateFieldCache.GetValue(am);
            if (cacheObj == null)
            {
                Status.Write("[MonoCache] Template field cache object is null.", StatusType.Warning);
                return;
            }

            try
            {
                // 1. Direct Remove(int) call on AssetsTools.NET.ConcurrentDictionary wrapper
                var removeMethod = cacheObj.GetType().GetMethod("Remove", new[] { typeof(int) });
                if (removeMethod != null)
                {
                    var result = removeMethod.Invoke(cacheObj, new object[] { (int)AssetClassID.MonoBehaviour });
                    Status.Write($"[MonoCache] Direct Remove(MonoBehaviour) executed. Removed: {result}", StatusType.Success);
                    return;
                }

                Status.Write("[MonoCache] Direct Remove not found, falling back to internal _dict...");
                var innerDictField = cacheObj.GetType().GetField("_dict", BindingFlags.Instance | BindingFlags.NonPublic);
                if (innerDictField != null)
                {
                    var innerDict = innerDictField.GetValue(cacheObj);
                    var tryRemoveMethod = innerDict?.GetType().GetMethod("TryRemove", new[] { typeof(int), typeof(AssetTypeTemplateField).MakeByRefType() });
                    if (tryRemoveMethod != null)
                    {
                        var args = new object?[] { (int)AssetClassID.MonoBehaviour, null };
                        var removed = (bool)(tryRemoveMethod.Invoke(innerDict, args) ?? false);
                        Status.Write($"[MonoCache] Internal _dict.TryRemove executed. Removed: {removed}", StatusType.Success);
                        return;
                    }
                }

                Status.Write("[MonoCache] Could not resolve any remove method on cache dictionary.", StatusType.Warning);
            }
            catch (Exception ex)
            {
                Status.Write($"[MonoCache] Exception while clearing mono cache: {ex.Message}", StatusType.Error);
            }
        }
    }
}