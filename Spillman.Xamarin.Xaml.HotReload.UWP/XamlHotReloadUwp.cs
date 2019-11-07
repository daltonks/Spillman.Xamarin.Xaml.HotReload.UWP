using System;
using System.Diagnostics;
using System.IO;
using Windows.Storage;
using Windows.Storage.Search;

namespace Spillman.Xamarin.Xaml.HotReload.UWP
{
    public static class XamlHotReloadUwp
    {
        public static void Init()
        {
            XamlHotReload.Init();

            XamlHotReload.WatchFileFunc = async path =>
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);

                    var fileBasicProperties = await file.GetBasicPropertiesAsync();
                    var previousModifiedTime = fileBasicProperties.DateModified;

                    var directory = Path.GetDirectoryName(path);
                    var options = new QueryOptions(CommonFileQuery.DefaultQuery, new[] {".xaml"});
                    var folder = await StorageFolder.GetFolderFromPathAsync(directory);
                    var query = folder.CreateFileQueryWithOptions(options);

                    await query.GetFilesAsync();

                    query.ContentsChanged += async (sender, args) =>
                    {
                        fileBasicProperties = await file.GetBasicPropertiesAsync();
                        var modifiedTime = fileBasicProperties.DateModified;

                        if (modifiedTime != previousModifiedTime)
                        {
                            var newXaml = await FileIO.ReadTextAsync(file);
                            await XamlHotReload.OnXamlChangedAsync(path, newXaml);
                            previousModifiedTime = modifiedTime;
                        }

                        await query.GetFilesAsync();
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            };
        }
    }
}
