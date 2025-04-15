using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;

namespace AppUpdater
{
    public class AppUpdateService
    {
        readonly SparkleUpdater _sparkle = default!;
        readonly ILogger? _logger;
        readonly Subject<ICancelEventArg> _subject = new();
        /// <summary>
        /// イベントハンドラDispose
        /// </summary>
        CompositeDisposable _disposables = new CompositeDisposable();
        /// <summary>
        /// AppcastURL
        /// </summary>
        Uri _appcastUrl = default!;
        /// <summary>
        /// 公開鍵パス
        /// </summary>
        FileInfo _publicKeyPath = default!;
        /// <summary>
        /// ダウンロードしたアップデートファイル
        /// </summary>
        FileInfo? _downloadFile;
        /// <summary>
        /// 更新情報
        /// </summary>
        AppCastItem? _appcastItem;
        /// <summary>
        /// Sparkleインスタンス
        /// </summary>
        public SparkleUpdater Sparkle { get => _sparkle; }
        /// <summary>
        /// クローズアクション
        /// </summary>
        public Action? AppCloseAction { get; init; }
        /// <summary>
        /// 準備完了
        /// </summary>
        public bool UpdateReady { get; protected set; }
        /// <summary>
        /// 準備完了通知
        /// </summary>
        public IObservable<UpdateEventArg> UpdateStandbyEvent { get; }
        /// <summary>
        /// アップデートステータスチェックイベント
        /// </summary>
        public IObservable<UpdateEventArg> UpdateCheckFinishedEvent { get; }
        /// <summary>
        /// ダウンロードの進捗イベント
        /// </summary>
        public IObservable<UpdateProgressEventArg> UpdaterDownloadProgressEvent { get; }

        public static AppUpdateService CreateAppUpdateService(string publicKey, Uri appcastUrl, Action? appclose,ILogger<AppUpdateService>? logger=null)
        {
            var key=new Ed25519Checker(SecurityMode.Unsafe, publicKey);
            return new AppUpdateService(key, appcastUrl, appclose, logger);
        }

        public static AppUpdateService CreateAppUpdateService(FileInfo publicKeyFile, Uri appcastUrl, Action? appclose, ILogger<AppUpdateService>? logger = null)
        {
            if (!publicKeyFile.Exists)
            {
                throw new FileNotFoundException($"publicKeyFile '{publicKeyFile.Name}' is not found.");
            }
            var key = new Ed25519Checker(SecurityMode.Unsafe, null , publicKeyFile.FullName);
            return new AppUpdateService(key, appcastUrl, appclose, logger);
        }

        private string CreateFilePath(string source,AppCastItem castItem)
        {
            if(castItem.DownloadLink is (null or ""))
            {
                return string.Empty;
            }
            var url = new Uri(castItem.DownloadLink);
            var filename = System.IO.Path.GetFileName(url.LocalPath);
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, filename);
            return path;
        }
        /// <summary>
        /// コンストラクタ
        /// </summary>
        private AppUpdateService(Ed25519Checker publicKey, Uri appcastUrl,Action? appclose,ILogger? logger)
        {

            _appcastUrl = appcastUrl;
            AppCloseAction = appclose;
            UpdateReady = false;
            _logger = logger;
            _sparkle = new SparkleUpdater
                (
                    _appcastUrl.AbsoluteUri,
                    publicKey
                )
            {
                UIFactory = null,
                RelaunchAfterUpdate = false
            };

            UpdateStandbyEvent = _subject.OfType<UpdateEventArg>().Where(t => t.State == UpdateState.UpdateStandby).AsObservable();//準備完了イベント
            UpdateCheckFinishedEvent = _subject.OfType<UpdateEventArg>().Where(t => t.IsCheckFinished).AsObservable();//チェック完了イベント
            UpdaterDownloadProgressEvent= _subject.OfType<UpdateProgressEventArg>().AsObservable();


            AddEvent();
        }
        /// <summary>
        /// イベント追加
        /// </summary>
        private void AddEvent()
        {
            _disposables.Add(//更新の確認
                Observable.FromEventPattern<UpdateDetectedEventArgs>(_sparkle,nameof(_sparkle.UpdateDetected)).Subscribe(async e=>
                    {//アップデートが見つかったとき
                        var arg = UpdateEventArg.AvailableUpdate(); 
                        _subject.OnNext(arg);
                        if (arg.IsCancel || e.EventArgs.AppCastItems.Count==0)
                        {
                            return;
                        }
                        _appcastItem = e.EventArgs.LatestVersion;
                        await _sparkle.InitAndBeginDownload(_appcastItem);//最新バージョンの取得
                        //e.EventArgs.NextAction = NextUpdateAction.PerformUpdateUnattended;
                    })
                );

            _disposables.Add(//更新のダウンロード開始
                Observable.FromEvent<ItemDownloadProgressEvent,UpdateProgressEventArg>(
                    handler => (sender, item, e) =>
                    {
                        var arg = new UpdateProgressEventArg(e.ProgressPercentage,DownloadState.Downloading);
                        handler(arg);
                    },
                    h => _sparkle.DownloadMadeProgress += h,
                    h => _sparkle.DownloadMadeProgress -= h
                    ).Subscribe(t => _subject.OnNext(t))
                );

            _disposables.Add(//更新のダウンロードのエラー
                Observable.FromEvent<DownloadErrorEvent, UpdateProgressEventArg>(
                    handler => (item, path, exception) =>
                    {
                        var arg = new UpdateProgressEventArg(0, DownloadState.Error, exception);
                        handler(arg);
                    },
                    h => _sparkle.DownloadHadError += h,
                    h => _sparkle.DownloadHadError -= h).
                    Subscribe(t => {
                        _appcastItem = null;
                        _subject.OnNext(t);

                    })
                );

            _disposables.Add(//更新のダウンロードキャンセル
                Observable.FromEvent<DownloadEvent,UpdateProgressEventArg>(
                    handler => (item, path) =>
                    {
                        var arg = new UpdateProgressEventArg(0, DownloadState.Cancel);
                        handler(arg);
                    },
                    h => _sparkle.DownloadCanceled += h,
                    h => _sparkle.DownloadCanceled += h
                    ).Subscribe(t =>
                    {
                        _appcastItem=null;
                        _subject.OnNext(t);
                    })
                );

            //_disposables.Add(//チェック完了イベント
            //    Observable.FromEventPattern<object, UpdateStatus>(_sparkle, nameof(_sparkle.UpdateCheckFinished))
            //    .Subscribe(async (t) =>
            //    {
            //        var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            //        if (updateInfo is null || !updateInfo.Updates.Any())
            //        {
            //            return;
            //        }

            //        var arg = t.EventArgs == UpdateStatus.UpdateAvailable ?
            //                UpdateEventArg.AvailableUpdate() : UpdateEventArg.NotAvailableUpdate();
            //        _subject.OnNext(arg);
            //        if (t.EventArgs != UpdateStatus.UpdateAvailable || arg.IsCancel)
            //        {
            //            return;
            //        }

            //        //var updateDate = updateInfo.Updates.Last();
            //        //await _sparkle.InitAndBeginDownload(updateDate);
            //    })
            //);

            _disposables.Add(//ダウンロード完了
                Observable.FromEventPattern<object, string>(_sparkle, nameof(_sparkle.DownloadFinished)).
                Subscribe((t) =>
                {
                    _downloadFile = new(t.EventArgs);
                    var arg = UpdateEventArg.StandbyUpdate(_downloadFile, "");
                    _subject.OnNext(arg);
                    UpdateReady = true;

                    if (arg.IsCancel || _appcastItem is null)
                    {
                        System.Diagnostics.Debug.WriteLine("download complete cancel");
                        return;
                    }
                    var filepath = CreateFilePath(t.EventArgs, _appcastItem);
                    System.IO.File.Delete(filepath);
                    System.IO.File.Move(t.EventArgs, filepath);
                    _sparkle.InstallUpdate(_appcastItem, filepath);
                })
            );

            if (AppCloseAction is not null)
            {//アプリケーションの終了イベント
                _disposables.Add(
                    Observable.FromEvent<CloseApplication, Unit>(
                        handler => () => handler(Unit.Default),
                        h => _sparkle.CloseApplication += h,
                        h => _sparkle.CloseApplication -= h).
                        Subscribe(_ => AppCloseAction.Invoke())
                    );
            }
        }

        public async Task UpdateDitectAsync()
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            if (updateInfo is null || !updateInfo.Updates.Any())
            {
                return;
            }
            _appcastItem= updateInfo.Updates.LastOrDefault();
        }

        /// <summary>
        /// アップデート実行
        /// </summary>
        public async Task UpdateAsync(Action<UpdateEventArg> action)
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            if (_sparkle is null && updateInfo is null)
            {
                throw new NullReferenceException($"NetSparkleUpdater requierd for update is null.");
            }
            if (!updateInfo.Updates.Any())
            {
                UpdateEventArg.NotAvailableUpdate();
                return;
            }
            var updateDate = updateInfo.Updates.First();
            var arg = _downloadFile is null ?
                UpdateEventArg.NotAvailableUpdate() : UpdateEventArg.StandbyUpdate(_downloadFile, updateDate.Version??"");

            action(arg);
            if (arg.IsCancel)
            {
                return;
            }
            await _sparkle.InstallUpdate(updateDate);
        }
        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            _disposables.Clear();
        }
    }
}
