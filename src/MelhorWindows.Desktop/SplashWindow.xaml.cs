using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MelhorWindows.Desktop;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionLabel = version is null
            ? "1.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        FooterTextBlock.Text = $"Desenvolvido por James B. - v{versionLabel}";
        UpdateState(0.08, "Preparando interface", "Organizando o Auralis para abrir o fluxo principal sem ruido visual.");
    }

    public void UpdateState(double progress, string title, string? detail)
    {
        var clampedProgress = Math.Clamp(progress, 0d, 1d);
        LoadingProgressBar.Value = clampedProgress * 100d;
        ProgressValueTextBlock.Text = $"{Math.Round(clampedProgress * 100d):00}%";
        ProgressHintTextBlock.Text = clampedProgress >= 1d
            ? "Abertura concluida. O painel principal sera exibido em seguida."
            : "A splash acompanha o startup real do app e fecha sozinha quando tudo estiver pronto.";

        PhaseBadgeTextBlock.Text = ResolvePhaseLabel(clampedProgress);
        AnimateStepChange(
            StepTitleTextBlock,
            title,
            durationMilliseconds: 220,
            verticalOffset: 6d);
        AnimateStepChange(
            StepDescriptionTextBlock,
            string.IsNullOrWhiteSpace(detail)
                ? "Aguarde enquanto o Auralis prepara a sessao atual."
                : detail,
            durationMilliseconds: 240,
            verticalOffset: 8d);
    }

    private static string ResolvePhaseLabel(double progress)
    {
        if (progress >= 1d)
        {
            return "Pronto";
        }

        if (progress >= 0.90d)
        {
            return "Fechamento";
        }

        if (progress >= 0.70d)
        {
            return "Atualizacao";
        }

        if (progress >= 0.45d)
        {
            return "Biblioteca";
        }

        if (progress >= 0.20d)
        {
            return "Contexto";
        }

        return "Startup";
    }

    private static void AnimateStepChange(TextBlock target, string text, int durationMilliseconds, double verticalOffset)
    {
        if (target.Text == text)
        {
            return;
        }

        target.Text = text;

        if (target.RenderTransform is not TranslateTransform translateTransform)
        {
            translateTransform = new TranslateTransform();
            target.RenderTransform = translateTransform;
        }

        target.Opacity = 0.35;
        translateTransform.Y = verticalOffset;

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        target.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1d, duration)
            {
                EasingFunction = easing
            });

        translateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0d, duration)
            {
                EasingFunction = easing
            });
    }
}
