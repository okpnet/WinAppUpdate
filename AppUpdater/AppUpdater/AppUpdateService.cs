using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

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
        /// <summary>
        /// ファクトリ
        /// </summary>
        /// <param name="publicKey"></param>
        /// <param name="appcastUrl"></param>
        /// <param name="appclose"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public static AppUpdateService CreateAppUpdateService(string publicKey, Uri appcastUrl, Action? appclose,ILogger<AppUpdateService>? logger=null)
        {
            var key=new Ed25519Checker(SecurityMode.Unsafe, publicKey);
            return new AppUpdateService(key, appcastUrl, appclose, logger);
        }
        /// <summary>
        /// ファクトリ
        /// </summary>
        /// <param name="publicKeyFile"></param>
        /// <param name="appcastUrl"></param>
        /// <param name="appclose"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static AppUpdateService CreateAppUpdateService(FileInfo publicKeyFile, Uri appcastUrl, Action? appclose, ILogger<AppUpdateService>? logger = null)
        {
            if (!publicKeyFile.Exists)
            {
                throw new FileNotFoundException($"publicKeyFile '{publicKeyFile.Name}' is not found.");
            }
            var key = new Ed25519Checker(SecurityMode.Unsafe, null , publicKeyFile.FullName);
            return new AppUpdateService(key, appcastUrl, appclose, logger);
        }
        /// <summary>
        /// プライベートコンストラクタ
        /// インスタンスの生成はファクトリメソッドから。
        /// </summary>
        /// <param name="source"></param>
        /// <param name="castItem"></param>
        /// <returns></returns>
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
        /// プライベートコンストラクタ
        /// インスタンスの生成はファクトリメソッドから。
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
            UpdateCheckFinishedEvent = _subject.OfType<UpdateEventArg>().Where(t => t.State != UpdateState.UpdateStandby).AsObservable();//チェック完了イベント
            UpdaterDownloadProgressEvent= _subject.OfType<UpdateProgressEventArg>().AsObservable();//ダウンロード進捗イベント

            AddEvent();
        }
        /// <summary>
        /// NetSparkleUpdaterのイベントへ購読者を追加する
        /// </summary>
        private void AddEvent()
        {
            _disposables.Add(//更新の確認
                Observable.FromEventPattern<UpdateDetectedEventArgs>(_sparkle,nameof(_sparkle.UpdateDetected)).Subscribe(async e=>
                    {//アップデートが見つかったとき
                        _appcastItem = e.EventArgs.LatestVersion;
                        var arg = UpdateEventArg.AvailableUpdate(_appcastItem?.Version??""); 
                        _subject.OnNext(arg);
                        if (arg.IsCancel || e.EventArgs.AppCastItems.Count==0)
                        {
                            return;
                        }
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

            _disposables.Add(//ダウンロード完了
                Observable.FromEventPattern<object, string>(_sparkle, nameof(_sparkle.DownloadFinished)).
                Subscribe(async (t) =>
                {
                    if(_appcastItem is null)
                    {
                        return;
                    }
                    _downloadFile = new(t.EventArgs);
                    var arg = UpdateEventArg.StandbyUpdate(_downloadFile, _appcastItem.Version??"");
                    _subject.OnNext(arg);
                    UpdateReady = true;

                    if (arg.IsCancel || _appcastItem is null)
                    {
                        _logger?.LogInformation("UPDATE CANCEL BY USER");
                        return;
                    }

                    if (arg.ShouldWait)
                    {
                        try
                        {
                            await arg.UpdateTriggerTask;
                        }
                        catch (TaskCanceledException)
                        {
                            _logger?.LogInformation("CONTINUE FOR UPDATE CANCEL BY USER");
                            return;
                        }
                    }

                    var filepath = CreateFilePath(t.EventArgs, _appcastItem);
                    System.IO.File.Delete(filepath);
                    System.IO.File.Move(t.EventArgs, filepath);
                    await _sparkle.InstallUpdate(_appcastItem, filepath);
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
        /// <summary>
        /// 更新の確認
        /// </summary>
        /// <returns></returns>
        public async void UpdateDitectAsync()
        {
            var updateInfo =await _sparkle.CheckForUpdatesQuietly();
            if (updateInfo is null || !updateInfo.Updates.Any())
            {
                return;
            }
            _appcastItem= updateInfo.Updates.LastOrDefault();
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
