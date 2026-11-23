using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UniFutsal.Application.Diagnostics;
using UniFutsal.Infrastructure.Diagnostics;

namespace UniFutsal.Desktop.Diagnostics
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class H0DiagnosticPresenter : MonoBehaviour
    {
        private const string EngineVersion = "0.1.0-h0";

        private VisualElement _statusBox;
        private Label _statusHeadline;
        private Label _recordMeta;
        private Label _pathMeta;
        private Label _versionMeta;
        private string _databasePath;

        private void OnEnable()
        {
            var uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                return;
            }

            _databasePath = Path.Combine(UnityEngine.Application.persistentDataPath, "h0_diagnostic.db");

            var root = uiDocument.rootVisualElement;
            root.Clear();

            var stylesheet = Resources.Load<StyleSheet>("UI/H0Diagnostic");
            if (stylesheet != null)
            {
                root.styleSheets.Add(stylesheet);
            }

            var screen = new VisualElement();
            screen.AddToClassList("screen");

            var card = new VisualElement();
            card.AddToClassList("card");

            var logoTexture = Resources.Load<Texture2D>("Brand/unifutsal-logo");
            if (logoTexture != null)
            {
                var logo = new Image { image = logoTexture, scaleMode = ScaleMode.ScaleToFit };
                logo.AddToClassList("logo");
                card.Add(logo);
            }

            var title = new Label("UniFutsal Manager");
            title.AddToClassList("title");
            card.Add(title);

            var subtitle = new Label("Hito H0 · Diagnóstico de almacenamiento local sin conexión");
            subtitle.AddToClassList("subtitle");
            card.Add(subtitle);

            _statusBox = new VisualElement();
            _statusBox.AddToClassList("status-box");

            _statusHeadline = new Label("Comprobando almacenamiento local...");
            _statusHeadline.AddToClassList("status-headline");
            _statusBox.Add(_statusHeadline);

            _recordMeta = new Label();
            _recordMeta.AddToClassList("meta");
            _statusBox.Add(_recordMeta);

            _versionMeta = new Label();
            _versionMeta.AddToClassList("meta");
            _statusBox.Add(_versionMeta);

            _pathMeta = new Label();
            _pathMeta.AddToClassList("meta");
            _statusBox.Add(_pathMeta);

            card.Add(_statusBox);

            var actions = new VisualElement();
            actions.AddToClassList("actions");

            var retryButton = new Button(RunDiagnostic) { text = "Repetir comprobación" };
            retryButton.AddToClassList("btn-primary");
            actions.Add(retryButton);

            var quitButton = new Button(QuitApp) { text = "Salir" };
            quitButton.AddToClassList("btn-secondary");
            actions.Add(quitButton);

            card.Add(actions);
            screen.Add(card);
            root.Add(screen);

            RunDiagnostic();
        }

        private void RunDiagnostic()
        {
            var store = new SqliteLocalDiagnosticStore(_databasePath);
            var useCase = new RunLocalDiagnosticUseCase(
                store: store,
                utcClock: () => DateTime.UtcNow,
                idGenerator: () => "win-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            var result = useCase.Execute(EngineVersion);

            _statusBox.RemoveFromClassList("status-ok");
            _statusBox.RemoveFromClassList("status-error");
            _statusHeadline.RemoveFromClassList("status-headline-ok");
            _statusHeadline.RemoveFromClassList("status-headline-error");

            if (result.IsAvailable && result.Record != null)
            {
                _statusBox.AddToClassList("status-ok");
                _statusHeadline.AddToClassList("status-headline-ok");
                _statusHeadline.text = "✓ Datos locales disponibles";
                _recordMeta.text = $"Registro verificado: {result.Record.Id} ({result.Record.RecordedUtcIso})";
                _versionMeta.text = $"Motor: {result.Record.EngineVersion} · Unity {UnityEngine.Application.unityVersion}";
                _pathMeta.text = $"Base de datos: {_databasePath}";
            }
            else
            {
                _statusBox.AddToClassList("status-error");
                _statusHeadline.AddToClassList("status-headline-error");
                _statusHeadline.text = "✕ No se pudo verificar el almacenamiento local";
                _recordMeta.text = $"Código de error: {result.ErrorCode}";
                _versionMeta.text = $"Motor: {EngineVersion} · Unity {UnityEngine.Application.unityVersion}";
                _pathMeta.text = $"Ruta intentada: {_databasePath}";
            }
        }

        private static void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }
    }
}
