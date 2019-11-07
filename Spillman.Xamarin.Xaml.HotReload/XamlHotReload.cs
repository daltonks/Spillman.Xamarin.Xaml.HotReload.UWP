using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Internals;
using Xamarin.Forms.Xaml;

namespace Spillman.Xamarin.Xaml.HotReload
{
    public static class XamlHotReload
    {
        public static event Action<Exception> LoadXamlException;

        public static Func<string, Task> WatchFileFunc { get; set; }

        private static readonly Dictionary<string, WatchedXaml> WatchedXamls = new Dictionary<string, WatchedXaml>();
        
        private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

        private static bool _initialized;

        public static void Init()
        {
#if DEBUG
            _initialized = true;

            var resourceProvider2Setter = typeof(ResourceLoader)
                .GetRuntimeProperties()
                .Single(property => property.Name == nameof(ResourceLoader.ResourceProvider2))
                .SetMethod;

            Debug.Assert(resourceProvider2Setter != null, nameof(resourceProvider2Setter) + " != null");
            resourceProvider2Setter.Invoke(null, new object[] { (Func<ResourceLoader.ResourceLoadingQuery, ResourceLoader.ResourceLoadingResponse>) ProvideResource });

            ResourceLoader.ResourceLoadingResponse ProvideResource(ResourceLoader.ResourceLoadingQuery query)
            {
                var watchedFile = WatchedXamls.Values
                    .SingleOrDefault(wf => wf.AssemblyName.FullName == query.AssemblyName.FullName && wf.ResourcePath == query.ResourcePath);

                var xaml = watchedFile?.Xaml;

                if (xaml == null)
                {
                    return null;
                }

                return new ResourceLoader.ResourceLoadingResponse
                {
                    ResourceContent = xaml,
                    UseDesignProperties = false
                };
            }
#endif
        }

        public static async void EnableHotReload<TElement>(
            this TElement visualElement, 
            [CallerFilePath] string callerFilePath = ""
        ) where TElement : Element
        {
            if (!_initialized)
            {
                return;
            }

            await Semaphore.WaitAsync();
            try
            {
                var xamlPath = callerFilePath.Substring(0, callerFilePath.Length - 3);

                if (!WatchedXamls.TryGetValue(xamlPath, out var watchedFile))
                {
                    watchedFile = WatchedXamls[xamlPath] = new WatchedXaml(xamlPath, typeof(TElement));

                    await WatchFileFunc.Invoke(xamlPath);
                }

                watchedFile.AddElement(visualElement);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public static async Task OnXamlChangedAsync(string xamlPath, string newXaml)
        {
            await Semaphore.WaitAsync();
            try
            {
                var watchedXaml = WatchedXamls[xamlPath];
                watchedXaml.OnXamlChanged(newXaml);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        private class WatchedXaml
        {
            public volatile string Xaml;

            public AssemblyName AssemblyName { get; }
            public string ResourcePath { get; }

            private readonly HashSet<WeakReference<Element>> _elements = new HashSet<WeakReference<Element>>();
            private readonly string _xamlPath;
            private readonly Type _type;

            public WatchedXaml(string xamlPath, Type type)
            {
                _xamlPath = xamlPath;
                _type = type;

                AssemblyName = type.Assembly.GetName();
                ResourcePath = type
                    .GetTypeInfo()
                    .Assembly
                    .GetCustomAttributes<XamlResourceIdAttribute>()
                    .First(attribute => attribute.Type == type)
                    .Path;
            }

            public void AddElement(Element element)
            {
                _elements.Add(new WeakReference<Element>(element));
            }

            public void OnXamlChanged(string newXaml)
            {
                Xaml = newXaml;

                Device.BeginInvokeOnMainThread(() => {
                    try
                    {
                        var initializeComponentMethods = _type.GetRuntimeMethods().Where(method => method.Name == "InitializeComponent").ToList();
                        var initializeComponentMethod = initializeComponentMethods.Single();

                        var visualElements = _elements.ToList();
                        foreach (var weakVisualElement in visualElements)
                        {
                            if (weakVisualElement.TryGetTarget(out var visualElement))
                            {
                                initializeComponentMethod.Invoke(visualElement, new object[0]);

                                if (visualElement is IHotReloadVisualElement hotReloadVisualElement)
                                {
                                    hotReloadVisualElement.OnHotReload();
                                }
                            }
                            else
                            {
                                _elements.Remove(weakVisualElement);
                            }
                        }

                        Debug.WriteLine($"{nameof(XamlHotReload)}: Reloaded {_type.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);

                        LoadXamlException?.Invoke(ex);
                    }
                });
            }
        }
    }
}
