using System.IO;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using MelhorWindows.Desktop.Providers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Authorization;
using MelhorWindows.Domain.Entities;
using MelhorWindows.Domain.Enums;
using MelhorWindows.Infrastructure.Imaging;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace MelhorWindows.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopServices _services = DesktopComposition.Create();
    private readonly LaunchOptions _launchOptions = LaunchOptions.Parse(Environment.GetCommandLineArgs());

    private string? _selectedFolderPath;
    private string? _selectedImagePath;
    private HistoryCardItem? _selectedHistoryItem;
    private int _selectedImageWidth;
    private int _selectedImageHeight;
    private AppPage _activePage = AppPage.Home;
    private IReadOnlyList<BuiltInIconItem> _allBuiltInIcons = Array.Empty<BuiltInIconItem>();

    private CancellationTokenSource? _previewGenerationCts;
    private int _busyOperations;
    private AppUpdateInfo? _availableUpdate;
    private bool _isInitialized;
    private bool _isUpdatingGameBoosterUi;
    private bool _canRevertGameBoosterSession;
    private GameBoosterDashboardSnapshot? _latestGameBoosterSnapshot;
    private GameBoosterAiPanelSnapshot? _latestLocalAiPanelSnapshot;
    private RustGameBoosterPanelSnapshot? _latestRustPanelSnapshot;

    public MainWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        MaxWidth = SystemParameters.WorkArea.Width - 24;
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        LoadLaunchContext();
        LoadCurrentUser();
        LoadEditorDefaults();
        CloseDashboard();
    }

    public async Task InitializeAsync(Action<double, string, string?>? reportProgress = null)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            ReportInitializationProgress(
                reportProgress,
                0.10,
                "Preparando ambiente",
                "Organizando os argumentos de abertura e a sessao local.");
            await HandleStartupCommandsAsync();

            ReportInitializationProgress(
                reportProgress,
                0.24,
                "Verificando pasta atual",
                string.IsNullOrWhiteSpace(_selectedFolderPath)
                    ? "Nenhuma pasta veio selecionada. O painel sera aberto pronto para escolha manual."
                    : "Conferindo a pasta atual e corrigindo referencias antigas de icone.");
            await RepairCurrentFolderIconIfNeededAsync(showSuccessMessage: true);

            ReportInitializationProgress(
                reportProgress,
                0.42,
                "Carregando biblioteca visual",
                "Preparando a galeria rapida de icones e atalhos da interface.");
            LoadBuiltInIconLibrary();
            UpdateSettingsVisibility();

            ReportInitializationProgress(
                reportProgress,
                0.60,
                "Sincronizando historico",
                "Recuperando os icones recentes prontos para reaplicar.");
            await LoadHistoryAsync();

            ReportInitializationProgress(
                reportProgress,
                0.72,
                "Montando JB GameBooster",
                "Lendo o catalogo inicial de otimizacoes, seguranca e reversao.");
            await LoadGameBoosterAsync(includeLocalAi: true);
            ApplyStartupSurface();

            ReportInitializationProgress(
                reportProgress,
                0.82,
                "Verificando atualizacoes",
                "Consultando o repositório para detectar uma versao mais nova do Auralis.");
            await CheckForUpdatesAsync(
                showNoUpdateMessage: _launchOptions.CheckForUpdates,
                showErrors: _launchOptions.CheckForUpdates);

            if (CanManageWindowsFeatures())
            {
                ReportInitializationProgress(
                    reportProgress,
                    0.94,
                    "Lendo recursos administrativos",
                    "Carregando estados do Windows e auditoria do perfil com permissao elevada.");
                await LoadFeatureStatesAsync();
                await LoadAuditAsync();
            }
            else
            {
                ReportInitializationProgress(
                    reportProgress,
                    0.94,
                    "Finalizando sessao",
                    "Perfil padrao carregado com foco no fluxo rapido de troca de icones.");
            }
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
            ReportInitializationProgress(
                reportProgress,
                1.00,
                "Inicializacao concluida com aviso",
                exception.Message);
            return;
        }

        ReportInitializationProgress(
            reportProgress,
            1.00,
            "Tudo pronto",
            "Abrindo o painel principal do Auralis.");

        if (_launchOptions.OpenDashboard)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _services.GameBoosterAiWorkflowService.AnalyzeRustAsync();
                    await _services.GameBoosterAiWorkflowService.AnalyzeAsync();
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (!_isUpdatingGameBoosterUi)
                        {
                            await LoadRustPanelAsync();
                            await LoadLocalAiPanelAsync();
                        }
                    });
                }
                catch { } // Silently fail background auto-start if Gemini is unavailable
            });
        }
    }

    private void ApplyStartupSurface()
    {
        if (_launchOptions.OpenDashboard)
        {
            ShowPage(AppPage.Home);
            return;
        }

        ShowPage(AppPage.IconEditor);
    }

    private void LoadLaunchContext()
    {
        _selectedFolderPath = _launchOptions.FolderPath;
        RefreshFolderPresentation();
    }

    private void LoadCurrentUser()
    {
        var roles = string.Join(", ", _services.UserContext.RoleNames);
        var initials = ResolveUserInitials(_services.UserContext.UserName);

        UserInitialsTextBlock.Text = initials;
        UserNameTextBlock.Text = _services.UserContext.UserName;
        UserRoleSummaryTextBlock.Text = roles;
        RolesTextBlock.Text = $"Usuario {_services.UserContext.UserName} ({roles})";
        SettingsSummaryTextBlock.Text = CanManageWindowsFeatures()
            ? "Este perfil pode acessar integracao com Explorer, recursos administrativos do Windows, auditoria e o novo modulo JB GameBooster."
            : "Este perfil ve apenas configuracoes relevantes ao uso diario. O JB GameBooster abre em modo leitura quando faltam permissoes administrativas.";
    }

    private void LoadEditorDefaults()
    {
        SelectedImageNameTextBlock.Text = "Nenhuma imagem selecionada";
        SelectedImageMetaTextBlock.Text = "Escolha uma imagem para gerar o icone.";
        CropXTextBox.Text = "0";
        CropYTextBox.Text = "0";
        CropWidthTextBox.Text = "0";
        CropHeightTextBox.Text = "0";
        SelectionSummaryCard.Visibility = Visibility.Collapsed;
        SourcePreviewImage.Source = null;
        GeneratedPreviewImage.Source = null;
        SourcePreviewPlaceholderTextBlock.Visibility = Visibility.Visible;
        GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Visible;
        UpdateAdjustmentVisibility();
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Home);

    private void GameBoosterNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.GameBooster);

    private void GbTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;

        GbContentDashboard.Visibility = Visibility.Collapsed;
        GbContentSystem.Visibility = Visibility.Collapsed;
        GbContentAi.Visibility = Visibility.Collapsed;

        var inactiveTabStyle = (Style)FindResource("TabButtonStyle");
        var activeTabStyle = (Style)FindResource("PrimaryButtonStyle");

        GbTabDashboardBtn.Style = inactiveTabStyle;
        GbTabSystemBtn.Style = inactiveTabStyle;
        GbTabAiBtn.Style = inactiveTabStyle;

        btn.Style = activeTabStyle;

        if (btn == GbTabDashboardBtn) GbContentDashboard.Visibility = Visibility.Visible;
        else if (btn == GbTabSystemBtn) GbContentSystem.Visibility = Visibility.Visible;
        else if (btn == GbTabAiBtn) GbContentAi.Visibility = Visibility.Visible;
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Settings);

    private void IconEditorNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.IconEditor);

    private void AccountButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Home);

    private void DashboardCloseButton_Click(object sender, RoutedEventArgs e) => CloseDashboard();

    private void WindowCloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void WindowHeader_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ShowPage(AppPage page)
    {
        _activePage = page;
        IconEditorView.Visibility = page == AppPage.IconEditor ? Visibility.Visible : Visibility.Collapsed;
        HomeView.Visibility = page == AppPage.Home ? Visibility.Visible : Visibility.Collapsed;
        GameBoosterView.Visibility = page == AppPage.GameBooster ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;

        PageTitleTextBlock.Text = page switch
        {
            AppPage.IconEditor => "Trocar Ícone",
            AppPage.Home => "Painel",
            AppPage.GameBooster => "JB GameBooster",
            AppPage.Settings => "Configurações",
            _ => "Auralis"
        };

        IconEditorNavButton.Style = (Style)FindResource(page == AppPage.IconEditor ? "ActiveNavButtonStyle" : "NavButtonStyle");
        HomeNavButton.Style = (Style)FindResource(page == AppPage.Home ? "ActiveNavButtonStyle" : "NavButtonStyle");
        GameBoosterNavButton.Style = (Style)FindResource(page == AppPage.GameBooster ? "ActiveNavButtonStyle" : "NavButtonStyle");
        SettingsNavButton.Style = (Style)FindResource(page == AppPage.Settings ? "ActiveNavButtonStyle" : "NavButtonStyle");
    }

    private void CloseDashboard()
    {
        ShowPage(AppPage.IconEditor);
    }

    private void UpdateSettingsVisibility()
    {
        var canManageExplorer = _services.AuthorizationService.HasPermission(DefaultPermissions.RegisterExplorerIntegration);
        var canManageWindows = CanManageWindowsFeatures();

        IntegrationSettingsCard.Visibility = canManageExplorer ? Visibility.Visible : Visibility.Collapsed;
        AdminFeatureSettingsCard.Visibility = canManageWindows ? Visibility.Visible : Visibility.Collapsed;
        AdminAuditSettingsCard.Visibility = canManageWindows ? Visibility.Visible : Visibility.Collapsed;
        UserSettingsInfoCard.Visibility = canManageWindows ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LoadBuiltInIconLibrary()
    {
        _allBuiltInIcons = Providers.IconLibraryProvider.GetBuiltInIcons();
        ApplyLibraryFilter();
    }

    private void ApplyLibraryFilter()
    {
        if (IconLibraryListBox is null)
        {
            return;
        }

        var query = IconLibrarySearchTextBox?.Text?.Trim();
        IconLibraryListBox.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _allBuiltInIcons
            : _allBuiltInIcons.Where(item => item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void RefreshFolderPresentation()
    {
        var hasFolder = !string.IsNullOrWhiteSpace(_selectedFolderPath);
        var folderLabel = hasFolder ? Path.GetFileName(_selectedFolderPath!.TrimEnd(Path.DirectorySeparatorChar)) : "Nenhuma pasta selecionada";

        CurrentFolderNameTextBlock.Text = folderLabel;
        PreviewFolderNameTextBlock.Text = folderLabel;
        FolderPathTextBlock.Text = hasFolder
            ? _selectedFolderPath
            : "Escolha uma pasta aqui ou abra o app pelo menu de contexto do Explorer.";
    }

    private void IconLibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyLibraryFilter();

    private async void IconLibraryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IconLibraryListBox.SelectedItem is not BuiltInIconItem iconItem)
        {
            return;
        }

        try
        {
            var imagePath = EnsureBuiltInIconImage(iconItem);
            await LoadSelectedImageAsync(imagePath, refreshPreview: true);
            SetStatus($"Ícone \"{iconItem.Label}\" preparado para aplicação.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a pasta que recebera o novo icone.",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(_selectedFolderPath)
                    ? _selectedFolderPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
                string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                SetStatus("Nenhuma pasta foi selecionada.", isError: false);
                return;
            }

            _selectedFolderPath = dialog.SelectedPath;
            RefreshFolderPresentation();
            var repaired = await RepairCurrentFolderIconIfNeededAsync(showSuccessMessage: true);

            if (!repaired)
            {
                SetStatus("Pasta alvo atualizada.", isError: false);
            }
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void ChooseImageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var imagePath = PickImage();

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                SetStatus("Nenhuma imagem foi selecionada.", isError: false);
                return;
            }

            await LoadSelectedImageAsync(imagePath, refreshPreview: true);
            SetStatus("Imagem carregada para edicao.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void UploadDropZone_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var imagePath = ResolveSingleDroppedImagePath(e);
        e.Effects = imagePath is null ? System.Windows.DragDropEffects.None : System.Windows.DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void UploadDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        try
        {
            var imagePath = ResolveSingleDroppedImagePath(e);

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                SetStatus("Solte apenas um arquivo de imagem suportado.", isError: true);
                return;
            }

            await LoadSelectedImageAsync(imagePath, refreshPreview: true);
            SetStatus("Imagem carregada para edicao.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void UpdatePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await UpdatePreviewAsync();
    }

    private async void ApplyIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            SetStatus("Selecione uma pasta antes de aplicar o icone.", isError: true);
            return;
        }

        if (_selectedHistoryItem is null &&
            string.IsNullOrWhiteSpace(_selectedImagePath))
        {
            SetStatus("Selecione uma imagem, um ícone recente ou um ícone da biblioteca antes de aplicar.", isError: true);
            return;
        }

        string? successMessage = null;

        try
        {
            BeginBusy("Aplicando icone e atualizando o Explorer...");
            SetStatus("Aplicando icone e atualizando o Explorer...", isError: false);

            if (_selectedHistoryItem is not null)
            {
                await _services.FolderIconIntegrationService.ApplyIconAsync(_selectedFolderPath, _selectedHistoryItem.Entry.StoredIconPath);
                SetStatus("Ícone recente aplicado na pasta atual.", isError: false);
                successMessage = "Ícone aplicado com sucesso. Fechando...";
            }
            else
            {
                var result = await _services.FolderIconWorkflowService.ExecuteAsync(
                    new ApplyFolderIconRequest(
                        _selectedFolderPath,
                        _selectedImagePath!,
                        ResolveFitMode(),
                        BuildCropSelection()));

                SetStatus(result.Message, isError: !result.Succeeded);

                if (result.Succeeded)
                {
                    await LoadHistoryAsync();
                    successMessage = "Ícone aplicado com sucesso. Fechando...";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore cancellation
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }

        if (!string.IsNullOrWhiteSpace(successMessage))
        {
            await ShowSuccessAndCloseAsync(successMessage);
        }
    }

    private async void RestoreDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            SetStatus("Selecione uma pasta antes de restaurar o icone padrao.", isError: true);
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            "Isso removera o icone personalizado da pasta e apagara o desktop.ini gerado pelo Auralis. Deseja continuar?",
            "Auralis",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        string? successMessage = null;

        try
        {
            BeginBusy("Restaurando a pasta ao padrao e atualizando o Explorer...");
            SetStatus("Restaurando a pasta ao padrao e atualizando o Explorer...", isError: false);

            await _services.FolderIconIntegrationService.RemoveIconAsync(_selectedFolderPath);
            SetStatus("Pasta restaurada ao icone padrao.", isError: false);
            successMessage = "Pasta restaurada ao icone padrao. Fechando...";
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }

        if (!string.IsNullOrWhiteSpace(successMessage))
        {
            await ShowSuccessAndCloseAsync(successMessage);
        }
    }

    private async void ApplyHistoryIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not HistoryCardItem historyItem)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            SetStatus("Selecione uma pasta antes de reutilizar um icone do historico.", isError: true);
            return;
        }

        string? successMessage = null;

        try
        {
            BeginBusy("Aplicando icone do historico e atualizando o Explorer...");
            SetStatus("Aplicando icone do historico e atualizando o Explorer...", isError: false);
            _services.AuthorizationService.EnsurePermission(DefaultPermissions.ApplyFolderIcons);

            await _services.FolderIconIntegrationService.ApplyIconAsync(_selectedFolderPath, historyItem.Entry.StoredIconPath);
            await _services.IconHistoryRepository.AddAsync(
                new FolderIconHistoryEntry(
                    Guid.NewGuid(),
                    _services.UserContext.UserId,
                    historyItem.Entry.IconAssetId,
                    _selectedFolderPath,
                    historyItem.Entry.StoredIconPath,
                    historyItem.Entry.SourceImagePath,
                    historyItem.Entry.StoredPreviewImagePath,
                    historyItem.Entry.FitMode,
                    DateTimeOffset.UtcNow));

            GeneratedPreviewImage.Source = historyItem.PreviewImage;
            GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
            PreviewModeTextBlock.Text = "Icone reaplicado do historico.";

            await LoadHistoryAsync();
            SetStatus("Icone do historico aplicado na pasta atual.", isError: false);
            successMessage = "Icone aplicado com sucesso. Fechando...";
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }

        if (!string.IsNullOrWhiteSpace(successMessage))
        {
            await ShowSuccessAndCloseAsync(successMessage);
        }
    }

    private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is not HistoryCardItem historyItem)
        {
            UpdateSelectionSummaryVisibility();
            return;
        }

        _selectedHistoryItem = historyItem;
        _selectedImagePath = null;
        IconLibraryListBox.SelectedItem = null;
        SelectedImageNameTextBlock.Text = historyItem.Title;
        SelectedImageMetaTextBlock.Text = historyItem.Subtitle;
        SourcePreviewImage.Source = historyItem.PreviewImage;
        SourcePreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        GeneratedPreviewImage.Source = historyItem.PreviewImage;
        GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        UpdateSelectionSummaryVisibility();
        PreviewModeTextBlock.Text = "Ícone recente selecionado para aplicar.";
    }

    private Task LoadSelectedImageAsync(string imagePath, bool refreshPreview)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("A imagem selecionada nao foi encontrada.", imagePath);
        }

        var imageInfo = ReadImageInfo(imagePath);
        var sourcePreview = CreateBitmapImageFromPath(imagePath, decodePixelWidth: 420);

        _selectedHistoryItem = null;
        HistoryListBox.SelectedItem = null;
        _selectedImagePath = imagePath;
        (_selectedImageWidth, _selectedImageHeight) = imageInfo;

        SelectedImageNameTextBlock.Text = Path.GetFileName(imagePath);
        SelectedImageMetaTextBlock.Text = _selectedImageWidth == _selectedImageHeight
            ? $"{_selectedImageWidth} x {_selectedImageHeight} • imagem quadrada"
            : $"{_selectedImageWidth} x {_selectedImageHeight} • ajuste de enquadramento disponivel";

        ApplyCenteredCrop();
        UpdateAdjustmentVisibility();
        UpdateSelectionSummaryVisibility();
        SourcePreviewImage.Source = sourcePreview;
        SourcePreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;

        // Skip auto-preview on load: the source image is already shown in the drop zone.
        // Preview (icon render) only happens on apply, avoiding a duplicate full conversion.
        if (refreshPreview)
        {
            GeneratedPreviewImage.Source = null;
            GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Visible;
        }

        return Task.CompletedTask;
    }

    private async Task UpdatePreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedImagePath))
        {
            SetStatus("Escolha uma imagem antes de atualizar o preview.", isError: true);
            return;
        }

        _previewGenerationCts?.Cancel();
        _previewGenerationCts?.Dispose();
        _previewGenerationCts = new CancellationTokenSource();
        var cancellationToken = _previewGenerationCts.Token;

        BeginBusy("Gerando previa...");
        try
        {
            SetStatus("Processando preview...", isError: false);

            var prepared = await _services.ImageIconConversionService.PrepareIconAsync(
                new PrepareIconRequest(
                    _selectedImagePath,
                    ResolveFitMode(),
                    BuildCropSelection(),
                    Path.GetFileNameWithoutExtension(_selectedImagePath)),
                cancellationToken);

            GeneratedPreviewImage.Source = CreateBitmapImageFromBytes(prepared.PreviewPngBytes);
            GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
            PreviewModeTextBlock.Text = DescribePreviewMode();
            SetStatus("Preview atualizado.", isError: false);
        }
        catch (OperationCanceledException)
        {
            // ignore: pode ter sido substituida por outra alteração do usuário.
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void UpdateAdjustmentVisibility()
    {
        if (ImageAdjustmentCard is null ||
            ManualCropPanel is null ||
            ManualCropCheckBox is null ||
            PreviewModeTextBlock is null ||
            AdjustmentHintTextBlock is null)
        {
            return;
        }

        var requiresAdjustment = _selectedImageWidth > 0 &&
            _selectedImageHeight > 0 &&
            _selectedImageWidth != _selectedImageHeight;

        ImageAdjustmentCard.Visibility = requiresAdjustment ? Visibility.Visible : Visibility.Collapsed;
        ManualCropPanel.Visibility = requiresAdjustment &&
            ResolveFitMode() == ImageFitMode.CropToSquare &&
            ManualCropCheckBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (!requiresAdjustment)
        {
            ManualCropCheckBox.IsChecked = false;
            PreviewModeTextBlock.Text = string.IsNullOrWhiteSpace(_selectedImagePath)
                ? "Escolha uma imagem para gerar o icone."
                : "A imagem ja e quadrada, entao nenhum ajuste extra foi exibido.";
            return;
        }

        AdjustmentHintTextBlock.Text = "A imagem nao e quadrada. Escolha entre enquadrar ou manter tudo visivel.";
        PreviewModeTextBlock.Text = DescribePreviewMode();
    }

    private void UpdateSelectionSummaryVisibility()
    {
        if (SelectionSummaryCard is null)
        {
            return;
        }

        SelectionSummaryCard.Visibility = string.IsNullOrWhiteSpace(_selectedImagePath) && _selectedHistoryItem is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private string DescribePreviewMode()
    {
        if (_selectedImageWidth > 0 && _selectedImageHeight > 0 && _selectedImageWidth == _selectedImageHeight)
        {
            return "A imagem ja esta em formato ideal para icone.";
        }

        return ResolveFitMode() == ImageFitMode.CropToSquare
            ? "O app vai enquadrar a imagem em formato quadrado."
            : "O app vai manter a imagem completa dentro do quadrado.";
    }

    private void ApplyCenteredCrop()
    {
        if (_selectedImageWidth <= 0 || _selectedImageHeight <= 0)
        {
            return;
        }

        var squareSide = Math.Min(_selectedImageWidth, _selectedImageHeight);
        var offsetX = (_selectedImageWidth - squareSide) / 2;
        var offsetY = (_selectedImageHeight - squareSide) / 2;

        CropXTextBox.Text = offsetX.ToString();
        CropYTextBox.Text = offsetY.ToString();
        CropWidthTextBox.Text = squareSide.ToString();
        CropHeightTextBox.Text = squareSide.ToString();
    }

    private CropSelection? BuildCropSelection()
    {
        if (ImageAdjustmentCard.Visibility != Visibility.Visible ||
            ResolveFitMode() != ImageFitMode.CropToSquare ||
            ManualCropCheckBox.IsChecked != true)
        {
            return null;
        }

        var requestedWidth = Math.Max(1, ParseCropValue(CropWidthTextBox.Text));
        var requestedHeight = Math.Max(1, ParseCropValue(CropHeightTextBox.Text));
        var safeWidth = Math.Min(requestedWidth, _selectedImageWidth);
        var safeHeight = Math.Min(requestedHeight, _selectedImageHeight);
        var safeX = Math.Clamp(ParseCropValue(CropXTextBox.Text), 0, Math.Max(_selectedImageWidth - safeWidth, 0));
        var safeY = Math.Clamp(ParseCropValue(CropYTextBox.Text), 0, Math.Max(_selectedImageHeight - safeHeight, 0));

        CropXTextBox.Text = safeX.ToString();
        CropYTextBox.Text = safeY.ToString();
        CropWidthTextBox.Text = safeWidth.ToString();
        CropHeightTextBox.Text = safeHeight.ToString();

        return new CropSelection(safeX, safeY, safeWidth, safeHeight);
    }

    private async void FitModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAdjustmentVisibility();

        if (!IsLoaded || string.IsNullOrWhiteSpace(_selectedImagePath))
        {
            return;
        }

        await UpdatePreviewAsync();
    }

    private async void ManualCropCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAdjustmentVisibility();

        if (!IsLoaded || string.IsNullOrWhiteSpace(_selectedImagePath))
        {
            return;
        }

        await UpdatePreviewAsync();
    }

    private async void CropField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded ||
            string.IsNullOrWhiteSpace(_selectedImagePath) ||
            ManualCropPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        await UpdatePreviewAsync();
    }

    private async void CenterCropButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCenteredCrop();
        await UpdatePreviewAsync();
    }

    private async Task HandleStartupCommandsAsync()
    {
        var appExecutablePath = Environment.ProcessPath;

        if (_launchOptions.RegisterFolderVerb && !string.IsNullOrWhiteSpace(appExecutablePath))
        {
            await _services.ExplorerVerbRegistrationService.RegisterFolderVerbAsync(appExecutablePath);
            SetStatus("Menu de pasta registrado via argumento de inicializacao.", isError: false);
        }

        if (_launchOptions.UnregisterFolderVerb)
        {
            await _services.ExplorerVerbRegistrationService.UnregisterFolderVerbAsync();
            SetStatus("Menu de pasta removido via argumento de inicializacao.", isError: false);
        }
    }

    private async void RegisterVerbButton_Click(object sender, RoutedEventArgs e)
    {
        var appExecutablePath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(appExecutablePath))
        {
            SetStatus("Nao foi possivel determinar o caminho do executavel atual.", isError: true);
            return;
        }

        try
        {
            await _services.ExplorerVerbRegistrationService.RegisterFolderVerbAsync(appExecutablePath);
            SetStatus("Menu de pasta registrado no Explorer.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void UnregisterVerbButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _services.ExplorerVerbRegistrationService.UnregisterFolderVerbAsync();
            SetStatus("Menu de pasta removido do Explorer.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void EnableFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        await SetSelectedFeatureStateAsync(enabled: true);
    }

    private async void DisableFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        await SetSelectedFeatureStateAsync(enabled: false);
    }

    private async void RefreshHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void RefreshFeaturesButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadFeatureStatesAsync();
    }

    private async void RefreshAuditButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAuditAsync();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(showNoUpdateMessage: true, showErrors: true);
    }

    private async Task LoadHistoryAsync()
    {
        var entries = await _services.IconHistoryRepository.GetByUserIdAsync(_services.UserContext.UserId);
        var historyItems = entries
            .Where(entry => File.Exists(entry.StoredIconPath))
            .Select(entry => new HistoryCardItem(
                entry,
                CreateBitmapImageFromPathForHistory(ResolveHistoryPreviewPath(entry)),
                Path.GetFileNameWithoutExtension(entry.SourceImagePath),
                entry.FitMode == ImageFitMode.CropToSquare ? "Enquadrado para icone" : "Imagem completa ajustada",
                entry.AppliedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")))
            .Take(5)
            .ToArray();

        HistoryListBox.ItemsSource = historyItems;
        HistoryEmptyStateBorder.Visibility = historyItems.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadFeatureStatesAsync()
    {
        if (!CanManageWindowsFeatures())
        {
            FeatureListView.ItemsSource = Array.Empty<object>();
            return;
        }

        var states = await _services.WindowsFeatureWorkflowService.GetStatesAsync();
        FeatureListView.ItemsSource = states
            .Select(state => new FeatureListItem(
                state.Id,
                $"{state.DisplayName} [{FormatStatus(state.Status)}] - {state.Description}"))
            .ToArray();
    }

    private async Task LoadAuditAsync()
    {
        if (!CanManageWindowsFeatures())
        {
            AuditListView.ItemsSource = Array.Empty<object>();
            return;
        }

        var entries = await _services.RegistryAuditRepository.GetRecentAsync();
        AuditListView.ItemsSource = entries
            .Select(entry => new RegistryAuditListItem(FormatAuditEntry(entry)))
            .ToArray();
    }

    private async Task LoadGameBoosterAsync(bool includeLocalAi = true)
    {
        var snapshot = await _services.GameBoosterWorkflowService.GetDashboardSnapshotAsync();
        _latestGameBoosterSnapshot = snapshot;
        var canManageChanges = CanManageWindowsFeatures();

        GameBoosterScoreTextBlock.Text = $"{snapshot.OptimizationScore}%";
        GameBoosterProfileTextBlock.Text = snapshot.ActiveProfileName;
        GameBoosterSummaryTextBlock.Text =
            $"{snapshot.OptimizedItemCount} de {snapshot.TotalItemCount} ajustes alinhados. O modulo atual ja consegue aplicar otimizacoes gerais com reversao da ultima sessao.";
        UpdateGameBoosterTelemetry(snapshot);
        GameBoosterReadOnlyBorder.Visibility = canManageChanges ? Visibility.Collapsed : Visibility.Visible;

        _isUpdatingGameBoosterUi = true;

        try
        {
            GameBoosterRestorePointCheckBox.IsChecked = snapshot.SafetySettings.CreateRestorePointBeforeApply;
        }
        finally
        {
            _isUpdatingGameBoosterUi = false;
        }

        GameBoosterLastSessionTextBlock.Text = snapshot.LastAppliedAtUtc is null
            ? "Nenhuma sessao registrada ainda."
            : $"Ultima sessao aplicada em {snapshot.LastAppliedAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}.";

        GameBoosterRestorePointInfoTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.LastRestorePointDescription)
            ? snapshot.SafetySettings.CreateRestorePointBeforeApply
                ? "Nenhum restore point recente do booster foi registrado ainda."
                : "Restore point automatico desativado nas preferencias."
            : $"Ultimo restore point solicitado pelo booster: {snapshot.LastRestorePointDescription}";

        _canRevertGameBoosterSession = canManageChanges && snapshot.LastAppliedAtUtc is not null;
        ApplyRecommendedGameBoosterButton.IsEnabled = canManageChanges;
        RevertGameBoosterSessionButton.IsEnabled = _canRevertGameBoosterSession;

        GameBoosterOptimizationItemsControl.ItemsSource = snapshot.Optimizations
            .Select(state => new GameBoosterListItem(
                state.Id,
                state.Title,
                state.Category,
                state.Description,
                $"Atual: {state.CurrentLabel}",
                $"Recomendado: {state.RecommendedLabel}",
                $"Impacto {state.ImpactLabel}",
                $"Risco {state.RiskLabel}",
                state.IsOptimized ? "Alinhado" : "Pendente",
                state.IsOptimized ? BoosterAlignedBrush : BoosterPendingBrush,
                state.RequiresRestart ? "Reinicio recomendado apos aplicar." : "Reinicio nao deve ser necessario para este item.",
                state.IsOptimized ? "Alinhado" : "Aplicar",
                canManageChanges && !state.IsOptimized))
            .ToArray();

        await LoadRustPanelAsync();

        if (includeLocalAi)
        {
            await LoadLocalAiPanelAsync();
        }
    }

    private async Task LoadLocalAiPanelAsync()
    {
        var panel = await _services.GameBoosterAiWorkflowService.GetPanelSnapshotAsync();
        _latestLocalAiPanelSnapshot = panel;
        var availableModels = panel.Availability.AvailableModels.ToList();
        var effectiveModel = ResolveEffectiveLocalAiModel(panel.Settings.ModelName, availableModels);

        LocalAiEndpointTextBox.Text = panel.Settings.EndpointUrl;
        LocalAiModelComboBox.ItemsSource = availableModels;
        LocalAiModelComboBox.Text = effectiveModel;

        LocalAiStatusTextBlock.Text = panel.Availability.IsReachable
            ? "Diagnostico inteligente pronto para uso."
            : NormalizeAiUserMessage(
                panel.Availability.StatusMessage,
                "O diagnostico inteligente nao esta disponivel nesta instalacao.");
        LocalAiCommandHintTextBlock.Text = !panel.Availability.IsReachable
            ? "Quando este recurso estiver habilitado, o app vai montar uma leitura pratica do sistema e dos ajustes sugeridos."
            : "Clique em 'Gerar diagnostico inteligente' para atualizar o resumo desta maquina.";
        LocalAiAnalysisMetaTextBlock.Text = panel.LastAnalysis is null
            ? "Nenhuma analise inteligente salva ainda."
            : $"Ultima leitura em {panel.LastAnalysis.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}.";

        LocalAiAnalysisSummaryTextBlock.Text = NormalizeAiUserMessage(
            panel.LastAnalysis?.ExecutiveSummary,
            "Execute o diagnostico para receber um resumo pratico do estado atual do sistema.");

        LocalAiRecommendationItemsControl.ItemsSource = panel.LastAnalysis?.Recommendations
            .Select(item => new LocalAiRecommendationListItem(
                item.Priority,
                item.Title,
                item.Reason,
                item.SuggestedAction,
                string.IsNullOrWhiteSpace(item.RelatedOptimizationId)
                    ? "Sem optimizationId especifico"
                    : item.RelatedOptimizationId))
            .ToArray() ?? Array.Empty<LocalAiRecommendationListItem>();
    }

    private async Task LoadRustPanelAsync()
    {
        var rustPanel = await _services.GameBoosterAiWorkflowService.GetRustPanelSnapshotAsync();
        _latestRustPanelSnapshot = rustPanel;
        var profile = rustPanel.Profile;

        RustModuleSummaryTextBlock.Text = profile.Summary;
        RustCpuTextBlock.Text = profile.CpuLabel;
        RustMemoryTextBlock.Text = $"{profile.TotalRamGb} GB detectados ({profile.MemoryTierLabel})";
        RustPriorityHintTextBlock.Text = profile.AvoidHighPriorityFlag
            ? "CPU com perfil X3D detectado: o preset evita `-high` por padrao."
            : "Sem restricao especial para `-high` no preset inicial.";
        RustLaunchOptionsTextBox.Text = profile.LaunchOptions;
        RustClientCommandsTextBox.Text = string.Join(Environment.NewLine, profile.RecommendedClientCommands);
        RustSteamPathTextBlock.Text = profile.SteamConfigDetected
            ? $"Steam detectado em: {profile.SteamLocalConfigPath}"
            : "Nao encontrei um `localconfig.vdf` do Steam neste perfil.";
        RustConfigPathTextBlock.Text = profile.ClientConfigDetected
            ? $"client.cfg encontrado em: {profile.ClientConfigPath}"
            : $"client.cfg esperado em: {profile.ClientConfigPath}";

        RustAiAnalysisMetaTextBlock.Text = rustPanel.LastAnalysis is null
            ? "Nenhuma analise de Rust salva ainda."
            : $"Ultima leitura em {rustPanel.LastAnalysis.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}.";

        RustAiSummaryTextBlock.Text = NormalizeAiUserMessage(
            rustPanel.LastAnalysis?.ExecutiveSummary,
            "O diagnostico avalia argumentos de inicializacao, memoria e folga do sistema para o Rust.");

        RustAiRecommendationItemsControl.ItemsSource = rustPanel.LastAnalysis?.Recommendations
            .Select(item => new LocalAiRecommendationListItem(
                item.Priority,
                item.Title,
                item.Reason,
                item.SuggestedAction,
                string.IsNullOrWhiteSpace(item.RelatedOptimizationId)
                    ? "Rust"
                    : item.RelatedOptimizationId))
            .ToArray() ?? Array.Empty<LocalAiRecommendationListItem>();
    }

    private void UpdateGameBoosterTelemetry(GameBoosterDashboardSnapshot snapshot)
    {
        var telemetry = snapshot.Telemetry;

        UpdateCircularGauge(OptimizationGaugeArc, telemetry.OptimizationGaugePercent);
        OptimizationGaugeValueTextBlock.Text = $"{snapshot.OptimizationScore}%";
        OptimizationGaugeHintTextBlock.Text = $"{snapshot.OptimizedItemCount} de {snapshot.TotalItemCount}";
        OptimizationGaugeSummaryTextBlock.Text = telemetry.OptimizationSummary;

        UpdateCircularGauge(FpsGaugeArc, telemetry.FpsGaugePercent);
        FpsGaugeValueTextBlock.Text = $"{telemetry.EstimatedProjectedFps}";
        FpsGaugeHintTextBlock.Text = $"+{telemetry.EstimatedFpsGain} FPS";
        FpsGaugeSummaryTextBlock.Text = $"Atual {telemetry.EstimatedCurrentFps} | Meta {telemetry.EstimatedProjectedFps}";

        UpdateCircularGauge(CpuGaugeArc, telemetry.CpuGaugePercent);
        CpuGaugeValueTextBlock.Text = $"{telemetry.CurrentCpuUsagePercent:0.#}%";
        CpuGaugeHintTextBlock.Text = $"apos {telemetry.EstimatedCpuUsageAfterPercent:0.#}%";
        CpuGaugeSummaryTextBlock.Text = telemetry.CpuSummary;

        UpdateCircularGauge(MemoryGaugeArc, telemetry.MemoryGaugePercent);
        MemoryGaugeValueTextBlock.Text = $"{telemetry.CurrentMemoryLoadPercent}%";
        MemoryGaugeHintTextBlock.Text = $"{telemetry.CurrentMemoryUsedGb:0.0} / {telemetry.TotalMemoryGb:0.0} GB";
        MemoryGaugeSummaryTextBlock.Text = telemetry.MemorySummary;

        GameBoosterTelemetryFootnoteTextBlock.Text =
            $"Varredura local: {telemetry.CpuLabel} | {telemetry.WindowsVersion} | FPS e ganhos apos otimizar sao estimativas heuristicas.";
    }

    private static void UpdateCircularGauge(System.Windows.Shapes.Path gaugePath, double percent)
    {
        gaugePath.Data = BuildCircularGaugeGeometry(percent);
    }

    private static Geometry BuildCircularGaugeGeometry(double percent)
    {
        var normalizedPercent = Math.Clamp(percent, 0d, 100d);

        if (normalizedPercent <= 0d)
        {
            return Geometry.Empty;
        }

        const double radius = 60d;
        var center = new System.Windows.Point(65d, 65d);
        var sweepAngle = Math.Max(0.1d, normalizedPercent / 100d * 359.99d);
        var startPoint = PointOnCircle(center, radius, -90d);
        var endPoint = PointOnCircle(center, radius, -90d + sweepAngle);

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new System.Windows.Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepAngle >= 180d
        });

        return new PathGeometry([figure]);
    }

    private static System.Windows.Point PointOnCircle(System.Windows.Point center, double radius, double angleInDegrees)
    {
        var angleInRadians = angleInDegrees * Math.PI / 180d;
        return new System.Windows.Point(
            center.X + radius * Math.Cos(angleInRadians),
            center.Y + radius * Math.Sin(angleInRadians));
    }

    private void ShowGameBoosterReport(string title, string subtitle, string content)
    {
        GameBoosterReportTitleTextBlock.Text = title;
        GameBoosterReportSubtitleTextBlock.Text = subtitle;
        GameBoosterReportTextBox.Text = content;
        GameBoosterReportBorder.Visibility = Visibility.Visible;
    }

    private static string BuildNoAiReportText()
    {
        return string.Join(
            Environment.NewLine,
            [
                "Nenhum relatorio de analise inteligente foi salvo ainda.",
                string.Empty,
                "Para gerar o relatorio:",
                "1. Rode o diagnostico inteligente no booster geral.",
                "2. Se quiser um foco extra em jogo, rode a leitura dedicada do Rust.",
                "3. Abra novamente este relatorio para revisar o resumo salvo."
            ]);
    }

    private string BuildAiReportText()
    {
        var builder = new StringBuilder();
        var boosterAnalysis = _latestLocalAiPanelSnapshot?.LastAnalysis;
        var rustAnalysis = _latestRustPanelSnapshot?.LastAnalysis;

        if (boosterAnalysis is null && rustAnalysis is null)
        {
            return BuildNoAiReportText();
        }

        builder.AppendLine("RELATORIO DE ANALISE INTELIGENTE");
        builder.AppendLine();

        if (boosterAnalysis is not null)
        {
            builder.AppendLine("DIAGNOSTICO GERAL");
            builder.AppendLine($"Gerado em: {boosterAnalysis.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
            builder.AppendLine($"Perfil recomendado: {boosterAnalysis.RecommendedProfile}");
            builder.AppendLine($"Prontidao: {boosterAnalysis.ReadinessLevel}");
            builder.AppendLine($"Resumo: {NormalizeAiUserMessage(boosterAnalysis.ExecutiveSummary, "Nao foi possivel montar o resumo desta leitura.")}");
            builder.AppendLine();

            AppendRecommendations(builder, boosterAnalysis.Recommendations);
        }
        else
        {
            builder.AppendLine("DIAGNOSTICO GERAL");
            builder.AppendLine("Nenhuma leitura geral do booster foi salva ainda.");
            builder.AppendLine();
        }

        if (rustAnalysis is not null)
        {
            builder.AppendLine("DIAGNOSTICO DE RUST");
            builder.AppendLine($"Gerado em: {rustAnalysis.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
            builder.AppendLine($"Resumo: {NormalizeAiUserMessage(rustAnalysis.ExecutiveSummary, "Nao foi possivel montar o resumo desta leitura de Rust.")}");
            builder.AppendLine($"Launch options: {rustAnalysis.LaunchOptionsSummary}");
            builder.AppendLine();

            AppendRecommendations(builder, rustAnalysis.Recommendations);
        }
        else
        {
            builder.AppendLine("DIAGNOSTICO DE RUST");
            builder.AppendLine("Nenhuma leitura de Rust foi salva ainda.");
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildComputerReportText()
    {
        if (_latestGameBoosterSnapshot is null)
        {
            return "A varredura local do JB GameBooster ainda nao foi carregada.";
        }

        var snapshot = _latestGameBoosterSnapshot;
        var telemetry = snapshot.Telemetry;
        var rustProfile = _latestRustPanelSnapshot?.Profile;
        var pendingOptimizations = snapshot.Optimizations.Where(item => !item.IsOptimized).ToArray();
        var alignedOptimizations = snapshot.Optimizations.Where(item => item.IsOptimized).ToArray();
        var builder = new StringBuilder();

        builder.AppendLine("RELATORIO DO COMPUTADOR");
        builder.AppendLine();
        builder.AppendLine("Resumo do sistema");
        builder.AppendLine($"CPU: {telemetry.CpuLabel}");
        builder.AppendLine($"Threads logicas: {telemetry.LogicalCoreCount}");
        builder.AppendLine($"Windows: {telemetry.WindowsVersion}");
        builder.AppendLine($"Memoria atual: {telemetry.CurrentMemoryUsedGb:0.0} / {telemetry.TotalMemoryGb:0.0} GB ({telemetry.CurrentMemoryLoadPercent}%)");
        builder.AppendLine($"CPU atual: {telemetry.CurrentCpuUsagePercent:0.0}%");
        builder.AppendLine();
        builder.AppendLine("Estimativas do booster");
        builder.AppendLine($"Otimizacao aplicada: {snapshot.OptimizationScore}% ({snapshot.OptimizedItemCount} de {snapshot.TotalItemCount})");
        builder.AppendLine($"FPS estimado: atual {telemetry.EstimatedCurrentFps} | apos pendentes {telemetry.EstimatedProjectedFps} | ganho potencial +{telemetry.EstimatedFpsGain}");
        builder.AppendLine($"CPU: atual {telemetry.CurrentCpuUsagePercent:0.0}% | apos pendentes {telemetry.EstimatedCpuUsageAfterPercent:0.0}% | alivio {telemetry.EstimatedCpuReliefPercent:0.0} ponto(s)");
        builder.AppendLine($"Memoria: atual {telemetry.CurrentMemoryUsedGb:0.0} GB | apos pendentes {telemetry.EstimatedMemoryUsedAfterGb:0.0} GB | economia {telemetry.EstimatedMemorySavingsGb:0.0} GB");
        builder.AppendLine();

        if (snapshot.LastAppliedAtUtc is not null)
        {
            builder.AppendLine($"Ultima sessao aplicada: {snapshot.LastAppliedAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastRestorePointDescription))
        {
            builder.AppendLine($"Restore point solicitado: {snapshot.LastRestorePointDescription}");
        }

        builder.AppendLine();
        builder.AppendLine("Achados da varredura");

        foreach (var highlight in telemetry.ScanHighlights)
        {
            builder.AppendLine($"- {highlight}");
        }

        if (rustProfile is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Achados do perfil Rust");
            builder.AppendLine($"Resumo: {rustProfile.Summary}");
            builder.AppendLine(rustProfile.ClientConfigDetected
                ? $"client.cfg detectado em: {rustProfile.ClientConfigPath}"
                : $"client.cfg esperado em: {rustProfile.ClientConfigPath}");
            builder.AppendLine(rustProfile.SteamConfigDetected
                ? $"Steam localconfig.vdf detectado em: {rustProfile.SteamLocalConfigPath}"
                : "Steam localconfig.vdf nao detectado neste perfil.");
            builder.AppendLine($"Launch options sugeridos: {rustProfile.LaunchOptions}");
        }

        builder.AppendLine();
        builder.AppendLine("Otimizacoes pendentes");

        if (pendingOptimizations.Length == 0)
        {
            builder.AppendLine("- Nenhuma otimizacao pendente.");
        }
        else
        {
            foreach (var optimization in pendingOptimizations)
            {
                builder.AppendLine(
                    $"- {optimization.Title} [{optimization.Category}] | Impacto {optimization.ImpactLabel} | Risco {optimization.RiskLabel} | {optimization.CurrentLabel}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Otimizacoes ja alinhadas");

        if (alignedOptimizations.Length == 0)
        {
            builder.AppendLine("- Nenhuma otimizacao aplicada ainda.");
        }
        else
        {
            foreach (var optimization in alignedOptimizations)
            {
                builder.AppendLine($"- {optimization.Title} [{optimization.Category}] | {optimization.CurrentLabel}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Observacao");
        builder.AppendLine("CPU e memoria sao leituras locais do momento. FPS e ganhos apos aplicar pendentes sao estimativas heuristicas do modulo, nao benchmark real ingame.");

        return builder.ToString().TrimEnd();
    }

    private static void AppendRecommendations(
        StringBuilder builder,
        IReadOnlyList<GameBoosterAiRecommendation> recommendations)
    {
        if (recommendations.Count == 0)
        {
            builder.AppendLine("Sem recomendacoes adicionais registradas.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("Recomendacoes");

        for (var index = 0; index < recommendations.Count; index++)
        {
            var recommendation = recommendations[index];
            builder.AppendLine($"{index + 1}. [{recommendation.Priority}] {recommendation.Title}");
            builder.AppendLine($"   Motivo: {recommendation.Reason}");
            builder.AppendLine($"   Acao: {recommendation.SuggestedAction}");

            if (!string.IsNullOrWhiteSpace(recommendation.RelatedOptimizationId))
            {
                builder.AppendLine($"   Relacionado: {recommendation.RelatedOptimizationId}");
            }

            builder.AppendLine();
        }
    }

    private async void RefreshGameBoosterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Atualizando leitura do JB GameBooster...");
            await LoadGameBoosterAsync(includeLocalAi: false);
            SetStatus("Leitura do JB GameBooster atualizada.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void ShowAiReportButton_Click(object sender, RoutedEventArgs e)
    {
        ShowGameBoosterReport(
            "Relatorio da IA",
            "Compila a leitura geral do JB GameBooster e a analise dedicada de Rust.",
            BuildAiReportText());
    }

    private void ShowComputerReportButton_Click(object sender, RoutedEventArgs e)
    {
        ShowGameBoosterReport(
            "Relatorio do computador",
            "Mostra a varredura local do PC, os ajustes detectados e as estimativas do booster.",
            BuildComputerReportText());
    }

    private void CloseGameBoosterReportButton_Click(object sender, RoutedEventArgs e)
    {
        GameBoosterReportBorder.Visibility = Visibility.Collapsed;
    }

    private async void ApplyRecommendedGameBoosterButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteGameBoosterOperationAsync(
            "Aplicando ajustes recomendados do JB GameBooster...",
            cancellationToken => _services.GameBoosterWorkflowService.ApplyRecommendedAsync(cancellationToken));
    }

    private async void RevertGameBoosterSessionButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteGameBoosterOperationAsync(
            "Revertendo a ultima sessao do JB GameBooster...",
            cancellationToken => _services.GameBoosterWorkflowService.RevertLastSessionAsync(cancellationToken));
    }

    private async void ApplyGameBoosterOptimizationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not string optimizationId)
        {
            return;
        }

        await ExecuteGameBoosterOperationAsync(
            "Aplicando ajuste selecionado do JB GameBooster...",
            cancellationToken => _services.GameBoosterWorkflowService.ApplyOptimizationAsync(optimizationId, cancellationToken));
    }

    private async void GameBoosterRestorePointCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingGameBoosterUi)
        {
            return;
        }

        try
        {
            var settings = new OptimizationSafetySettings(GameBoosterRestorePointCheckBox.IsChecked == true);
            await _services.GameBoosterWorkflowService.SaveSafetySettingsAsync(settings);
            await LoadGameBoosterAsync(includeLocalAi: false);
            SetStatus("Preferencia de restore point atualizada.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void TestLocalAiConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Verificando disponibilidade do diagnostico inteligente...");
            await SaveLocalAiSettingsFromFormAsync();
            var result = await _services.GameBoosterAiWorkflowService.TestConnectionAsync();
            await LoadLocalAiPanelAsync();
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Diagnostico inteligente pronto para uso."
                        : "O diagnostico inteligente nao esta disponivel nesta instalacao."),
                isError: !result.Succeeded);
        }
        catch (Exception exception)
        {
            SetStatus(
                NormalizeAiUserMessage(
                    exception.Message,
                    "O diagnostico inteligente nao esta disponivel nesta instalacao."),
                isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async void SaveLocalAiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveLocalAiSettingsFromFormAsync();
            await LoadLocalAiPanelAsync();
            await LoadRustPanelAsync();
            SetStatus("Configuracao interna do diagnostico atualizada.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(
                NormalizeAiUserMessage(
                    exception.Message,
                    "Nao foi possivel atualizar a configuracao interna do diagnostico."),
                isError: true);
        }
    }

    private async void RunLocalAiAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Executando diagnostico inteligente...");
            await SaveLocalAiSettingsFromFormAsync();

            var result = await _services.GameBoosterAiWorkflowService.AnalyzeAsync();
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Diagnostico inteligente concluido."
                        : "Nao foi possivel concluir o diagnostico inteligente."),
                isError: !result.Succeeded);

            await LoadLocalAiPanelAsync();
        }
        catch (Exception exception)
        {
            SetStatus(
                NormalizeAiUserMessage(
                    exception.Message,
                    "Nao foi possivel concluir o diagnostico inteligente."),
                isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async void AnalyzeAndApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("IA analisando o PC...");
            await SaveLocalAiSettingsFromFormAsync();

            // Passo 1: IA analisa e salva o diagnóstico (sem aplicar ainda)
            var analyzeResult = await _services.GameBoosterAiWorkflowService.AnalyzeAsync();
            await LoadLocalAiPanelAsync();

            if (!analyzeResult.Succeeded)
            {
                SetStatus(
                    NormalizeAiUserMessage(
                        analyzeResult.Message,
                        "Nao foi possivel concluir o diagnostico inteligente."),
                    isError: true);
                return;
            }

            // Passo 2: Coletar as otimizações pendentes e exibir o painel de confirmação
            var pending = await _services.GameBoosterWorkflowService.GetPendingOptimizationsAsync();

            if (pending.Count == 0)
            {
                SetStatus("Sistema ja esta totalmente otimizado! Nenhuma acao pendente.", isError: false);
                ConfirmationPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Popular o painel de preview
            ConfirmationSummaryTextBlock.Text =
                $"A leitura inteligente encontrou {pending.Count} otimizacao(s) pendente(s). Revise abaixo e confirme para aplicar:";

            ConfirmationItemsControl.ItemsSource = pending
                .Select(item => new { item.Title, Description = $"{item.Description}  |  Impacto: {item.ImpactLabel}  |  Risco: {item.RiskLabel}" })
                .ToArray();

            ConfirmationPanel.Visibility = Visibility.Visible;
            SetStatus($"{pending.Count} otimizacao(s) prontas para aplicar. Confirme no painel.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(
                NormalizeAiUserMessage(
                    exception.Message,
                    "Nao foi possivel concluir o diagnostico inteligente."),
                isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async void ConfirmApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfirmationPanel.Visibility = Visibility.Collapsed;
            BeginBusy("Aplicando otimizacoes no sistema...");

            var result = await _services.GameBoosterWorkflowService.ApplyRecommendedAsync();
            SetStatus(result.Message, isError: !result.Succeeded);

            await LoadGameBoosterAsync(includeLocalAi: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void CancelApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmationPanel.Visibility = Visibility.Collapsed;
        SetStatus("Operacao cancelada pelo usuario.", isError: false);
    }

    private async void RunRustLocalAiAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Executando analise do perfil de Rust...");
            await SaveLocalAiSettingsFromFormAsync();

            var result = await _services.GameBoosterAiWorkflowService.AnalyzeRustAsync();
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Diagnostico dedicado de Rust concluido."
                        : "Nao foi possivel concluir o diagnostico de Rust."),
                isError: !result.Succeeded);

            await LoadRustPanelAsync();
            await LoadLocalAiPanelAsync();
        }
        catch (Exception exception)
        {
            SetStatus(
                NormalizeAiUserMessage(
                    exception.Message,
                    "Nao foi possivel concluir o diagnostico de Rust."),
                isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task ExecuteGameBoosterOperationAsync(
        string busyMessage,
        Func<CancellationToken, Task<OperationResult<GameBoosterDashboardSnapshot>>> operation)
    {
        try
        {
            BeginBusy(busyMessage);
            SetStatus(busyMessage, isError: false);

            var result = await operation(CancellationToken.None);
            SetStatus(result.Message, isError: !result.Succeeded);

            await LoadGameBoosterAsync(includeLocalAi: false);

            if (CanManageWindowsFeatures())
            {
                await LoadFeatureStatesAsync();
                await LoadAuditAsync();
            }
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private Task SaveLocalAiSettingsFromFormAsync()
    {
        var fallbackSettings = _latestLocalAiPanelSnapshot?.Settings ?? LocalAiConnectionSettings.Default;
        var endpoint = string.IsNullOrWhiteSpace(LocalAiEndpointTextBox.Text)
            ? fallbackSettings.EndpointUrl
            : LocalAiEndpointTextBox.Text.Trim();
        var model = string.IsNullOrWhiteSpace(LocalAiModelComboBox.Text)
            ? fallbackSettings.ModelName
            : LocalAiModelComboBox.Text.Trim();

        return _services.GameBoosterAiWorkflowService.SaveSettingsAsync(
            new LocalAiConnectionSettings(endpoint, model));
    }

    private static string NormalizeAiUserMessage(string? message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return fallback;
        }

        return ContainsAiTechnicalDetails(message) ? fallback : message;
    }

    private static bool ContainsAiTechnicalDetails(string message)
    {
        return message.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Google AI Studio", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("API", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("modelo", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("model", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Ollama", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("pull", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEffectiveLocalAiModel(string configuredModel, IReadOnlyList<string> availableModels)
    {
        if (availableModels.Contains(configuredModel, StringComparer.OrdinalIgnoreCase))
        {
            return configuredModel;
        }

        return SelectPreferredLocalAiModel(availableModels) ?? configuredModel;
    }

    private static string? SelectPreferredLocalAiModel(IReadOnlyList<string> availableModels)
    {
        if (availableModels.Count == 0)
        {
            return null;
        }

        string[] preferredModels =
        [
            "gemini-2.5-flash",
            "gemini-2.5-pro",
            "gemini-2.0-flash"
        ];

        foreach (var preferredModel in preferredModels)
        {
            var exactMatch = availableModels.FirstOrDefault(model =>
                string.Equals(model, preferredModel, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(exactMatch))
            {
                return exactMatch;
            }
        }

        return availableModels.FirstOrDefault(model =>
                   !model.Contains("-cloud", StringComparison.OrdinalIgnoreCase)) ??
               availableModels[0];
    }

    private async Task SetSelectedFeatureStateAsync(bool enabled)
    {
        if (FeatureListView.SelectedItem is not FeatureListItem selectedFeature)
        {
            SetStatus("Selecione uma feature do Windows antes de continuar.", isError: true);
            return;
        }

        try
        {
            var result = await _services.WindowsFeatureWorkflowService.SetStateAsync(selectedFeature.Id, enabled);
            SetStatus(result.Message, isError: !result.Succeeded);
            await LoadFeatureStatesAsync();
            await LoadAuditAsync();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private bool CanManageWindowsFeatures() =>
        _services.AuthorizationService.HasPermission(DefaultPermissions.EditWindowsRegistry);

    private async Task CheckForUpdatesAsync(bool showNoUpdateMessage, bool showErrors)
    {
        try
        {
            var updateInfo = await _services.AppUpdateService.CheckForUpdatesAsync();

            if (updateInfo.IsUpdateAvailable)
            {
                ShowUpdateNotice(updateInfo);
                SetStatus(
                    $"Atualizacao disponivel no GitHub. Commit {updateInfo.LatestCommitShortSha} encontrado para o Auralis.",
                    isError: false);
                return;
            }

            HideUpdateNotice();

            if (showNoUpdateMessage)
            {
                SetStatus(
                    $"Auralis atualizado. Versao atual {updateInfo.CurrentVersionLabel}.",
                    isError: false);
            }
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                SetStatus($"Nao foi possivel verificar atualizacoes: {exception.Message}", isError: true);
            }
        }
    }

    private void ShowUpdateNotice(AppUpdateInfo updateInfo)
    {
        _availableUpdate = updateInfo;
        UpdateNoticeTextBlock.Text =
            $"Atualizacao disponivel. Atual: {updateInfo.CurrentVersionLabel} ({updateInfo.CurrentCommitShortSha}) • GitHub: {updateInfo.LatestCommitShortSha} em {updateInfo.LatestCommitDateUtc.ToLocalTime():dd/MM/yyyy HH:mm}.";
        UpdateNoticeBorder.Visibility = Visibility.Visible;
    }

    private void HideUpdateNotice()
    {
        _availableUpdate = null;
        UpdateNoticeTextBlock.Text = string.Empty;
        UpdateNoticeBorder.Visibility = Visibility.Collapsed;
    }

    private void OpenUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _availableUpdate.LatestCommitUrl,
            UseShellExecute = true
        });
    }

    private async Task ShowSuccessAndCloseAsync(string message)
    {
        SetStatus(message, isError: false);
        await CloseAfterSuccessAsync();
    }

    private async Task<bool> RepairCurrentFolderIconIfNeededAsync(bool showSuccessMessage)
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            return false;
        }

        var repaired = await _services.FolderIconIntegrationService.RepairIconReferenceAsync(_selectedFolderPath);

        if (repaired && showSuccessMessage)
        {
            SetStatus("O Auralis normalizou automaticamente o icone salvo nesta pasta para o formato local e estavel.", isError: false);
        }

        return repaired;
    }

    private async Task CloseAfterSuccessAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(1.15));
        Close();
    }

    private static string ResolveUserInitials(string userName)
    {
        var parts = userName
            .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return parts.Length == 0 ? "MW" : new string(parts);
    }

    private static int ParseCropValue(string? text) =>
        int.TryParse(text, out var value) ? value : 0;

    private ImageFitMode ResolveFitMode()
    {
        var selectedItem = FitModeComboBox.SelectedItem as ComboBoxItem;
        var selectedTag = selectedItem?.Tag?.ToString();

        return string.Equals(selectedTag, "FitInsideSquare", StringComparison.Ordinal)
            ? ImageFitMode.FitInsideSquare
            : ImageFitMode.CropToSquare;
    }

    private string? PickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = ImageAssetFormats.SupportedImageDialogFilter,
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog(this) == true
            ? dialog.FileName
            : null;
    }

    private static string? ResolveSingleDroppedImagePath(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return null;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files ||
            files.Length != 1)
        {
            return null;
        }

        return IsSupportedImagePath(files[0]) ? files[0] : null;
    }

    private static bool IsSupportedImagePath(string? path)
    {
        return ImageAssetFormats.IsSupportedPath(path);
    }

    private string EnsureBuiltInIconImage(BuiltInIconItem iconItem)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "Auralis", "LibraryIcons");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, $"{iconItem.Id}.png");
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formattedText = new FormattedText(
            iconItem.Glyph,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            224,
            System.Windows.Media.Brushes.White,
            pixelsPerDip);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(iconItem.TileBackgroundBrush, null, new Rect(0, 0, 512, 512), 104, 104);
            context.DrawText(formattedText, new System.Windows.Point((512 - formattedText.Width) / 2, (512 - formattedText.Height) / 2 - 8));
        }

        var bitmap = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        return outputPath;
    }

    private static SolidColorBrush CreateBrush(string hexColor)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor)!);
        brush.Freeze();
        return brush;
    }

    private static (int Width, int Height) ReadImageInfo(string imagePath)
    {
        try
        {
            if (ImageAssetFormats.IsSvgPath(imagePath))
            {
                return SvgRasterizer.ReadImageInfo(imagePath);
            }

            return ImageAssetFormats.UsesImageSharpPipeline(imagePath)
                ? ReadImageInfoWithImageSharp(imagePath)
                : ReadImageInfoWithWpfDecoder(imagePath);
        }
        catch (Exception exception) when (ImageAssetFormats.IsUnsupportedImageException(exception))
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage, exception);
        }
    }

    private static BitmapImage CreateBitmapImageFromPath(string imagePath, int decodePixelWidth)
    {
        try
        {
            if (ImageAssetFormats.IsSvgPath(imagePath))
            {
                return CreateBitmapImageFromBytes(SvgRasterizer.RenderPreviewToPng(imagePath, decodePixelWidth));
            }

            return ImageAssetFormats.UsesImageSharpPipeline(imagePath)
                ? CreateBitmapImageFromPathWithImageSharp(imagePath, decodePixelWidth)
                : CreateBitmapImageFromPathWithWpfDecoder(imagePath, decodePixelWidth);
        }
        catch (Exception exception) when (ImageAssetFormats.IsUnsupportedImageException(exception))
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage, exception);
        }
    }

    private static BitmapImage CreateBitmapImageFromPath(string imagePath)
    {
        return CreateBitmapImageFromPath(imagePath, decodePixelWidth: 0);
    }

    private static BitmapImage CreateBitmapImageFromPathForHistory(string imagePath)
    {
        // History previews: reduzir custo de decode e atrasar criação para não travar UI.
        return CreateBitmapImageFromPath(imagePath, decodePixelWidth: 220);
    }

    private static string ResolveHistoryPreviewPath(FolderIconHistoryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.StoredPreviewImagePath) &&
            File.Exists(entry.StoredPreviewImagePath))
        {
            return entry.StoredPreviewImagePath;
        }

        if (!string.IsNullOrWhiteSpace(entry.SourceImagePath) &&
            File.Exists(entry.SourceImagePath))
        {
            return entry.SourceImagePath;
        }

        return entry.StoredIconPath;
    }

    private static BitmapImage CreateBitmapImageFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static (int Width, int Height) ReadImageInfoWithImageSharp(string imagePath)
    {
        using var stream = OpenImageReadStream(imagePath);
        var info = ImageSharpImage.Identify(stream);

        if (info is null)
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage);
        }

        return (info.Width, info.Height);
    }

    private static (int Width, int Height) ReadImageInfoWithWpfDecoder(string imagePath)
    {
        using var stream = OpenImageReadStream(imagePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.None);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage);
        }

        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static BitmapImage CreateBitmapImageFromPathWithImageSharp(string imagePath, int decodePixelWidth)
    {
        using var stream = OpenImageReadStream(imagePath);
        using var image = ImageSharpImage.Load(stream);

        if (decodePixelWidth > 0 &&
            (image.Width > decodePixelWidth || image.Height > decodePixelWidth))
        {
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3,
                Size = new ImageSharpSize(decodePixelWidth, decodePixelWidth)
            }));
        }

        using var normalizedStream = new MemoryStream();
        image.Save(normalizedStream, new PngEncoder());
        return CreateBitmapImageFromBytes(normalizedStream.ToArray());
    }

    private static BitmapImage CreateBitmapImageFromPathWithWpfDecoder(string imagePath, int decodePixelWidth)
    {
        using var stream = OpenImageReadStream(imagePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;

        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static FileStream OpenImageReadStream(string imagePath) =>
        File.Open(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    private static readonly SolidColorBrush StatusSuccessBackgroundBrush =
        new(System.Windows.Media.Color.FromRgb(0x10, 0x2A, 0x22));
    private static readonly SolidColorBrush StatusSuccessBorderBrush =
        new(System.Windows.Media.Color.FromRgb(0x2A, 0x59, 0x46));
    private static readonly SolidColorBrush StatusSuccessTextBrush =
        new(System.Windows.Media.Color.FromRgb(0xA5, 0xF4, 0xCB));

    private static readonly SolidColorBrush StatusErrorBackgroundBrush =
        new(System.Windows.Media.Color.FromRgb(0x4B, 0x17, 0x25));
    private static readonly SolidColorBrush StatusErrorBorderBrush =
        new(System.Windows.Media.Color.FromRgb(0x7F, 0x27, 0x37));
    private static readonly SolidColorBrush StatusErrorTextBrush =
        new(System.Windows.Media.Color.FromRgb(0xFF, 0xB0, 0xBA));
    private static readonly SolidColorBrush BoosterAlignedBrush =
        new(System.Windows.Media.Color.FromRgb(0x1E, 0x5C, 0x47));
    private static readonly SolidColorBrush BoosterPendingBrush =
        new(System.Windows.Media.Color.FromRgb(0x77, 0x59, 0x21));

    static MainWindow()
    {
        // Reaproveita instâncias para reduzir alocações e micro-travamentos.
        StatusSuccessBackgroundBrush.Freeze();
        StatusSuccessBorderBrush.Freeze();
        StatusSuccessTextBrush.Freeze();
        StatusErrorBackgroundBrush.Freeze();
        StatusErrorBorderBrush.Freeze();
        StatusErrorTextBrush.Freeze();
        BoosterAlignedBrush.Freeze();
        BoosterPendingBrush.Freeze();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusBorder.Background = isError ? StatusErrorBackgroundBrush : StatusSuccessBackgroundBrush;
        StatusBorder.BorderBrush = isError ? StatusErrorBorderBrush : StatusSuccessBorderBrush;
        StatusTextBlock.Foreground = isError ? StatusErrorTextBrush : StatusSuccessTextBrush;
        StatusTextBlock.Text = isError ? $"Erro: {message}" : message;
    }

    private static string FormatStatus(WindowsFeatureStatus status) =>
        status switch
        {
            WindowsFeatureStatus.Enabled => "Enabled",
            WindowsFeatureStatus.Disabled => "Disabled",
            WindowsFeatureStatus.Custom => "Custom",
            _ => "Unknown"
        };

    private static string FormatAuditEntry(RegistryChangeAuditEntry entry)
    {
        var hiveName = entry.Hive switch
        {
            RegistryHive.CurrentUser => "HKCU",
            RegistryHive.LocalMachine => "HKLM",
            _ => entry.Hive.ToString()
        };

        var actionText = entry.WasDeleted
            ? "deleted"
            : $"set to {entry.NewValue ?? "(null)"}";

        return $"{entry.ChangedAtUtc:yyyy-MM-dd HH:mm:ss} | {hiveName}\\{entry.KeyPath}\\{entry.ValueName} | {actionText}";
    }

    private sealed record FeatureListItem(string Id, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record HistoryCardItem(
        FolderIconHistoryEntry Entry,
        BitmapImage PreviewImage,
        string Title,
        string Subtitle,
        string AppliedText);



    private sealed record RegistryAuditListItem(string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record GameBoosterListItem(
        string Id,
        string Title,
        string Category,
        string Description,
        string CurrentText,
        string RecommendedText,
        string ImpactText,
        string RiskText,
        string StatusText,
        System.Windows.Media.Brush StatusBrush,
        string RestartText,
        string ApplyButtonText,
        bool CanApply);

    private sealed record LocalAiRecommendationListItem(
        string Priority,
        string Title,
        string Reason,
        string SuggestedAction,
        string RelatedOptimizationText);

    private enum AppPage
    {
        IconEditor,
        Home,
        History,
        GameBooster,
        Settings
    }

    private static void ReportInitializationProgress(
        Action<double, string, string?>? reportProgress,
        double progress,
        string title,
        string? detail)
    {
        reportProgress?.Invoke(progress, title, detail);
    }

    private void BeginBusy(string message = "Processando alteracao...")
    {
        _busyOperations++;
        SetBusyMessage(message);
        ApplyBusyState();
    }

    private void EndBusy()
    {
        _busyOperations = Math.Max(0, _busyOperations - 1);
        ApplyBusyState();
    }

    private void ApplyBusyState()
    {
        var enabled = _busyOperations == 0;
        var isBusy = !enabled;

        if (BusyOverlayRoot != null)
        {
            BusyOverlayRoot.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ApplyIconButton != null) ApplyIconButton.IsEnabled = enabled;
        if (RestoreDefaultButton != null) RestoreDefaultButton.IsEnabled = enabled;
        if (UpdatePreviewButton != null) UpdatePreviewButton.IsEnabled = enabled;
        if (ChooseImageButton != null) ChooseImageButton.IsEnabled = enabled;
        if (ReplaceImageButton != null) ReplaceImageButton.IsEnabled = enabled;
        if (ApplyRecommendedGameBoosterButton != null) ApplyRecommendedGameBoosterButton.IsEnabled = enabled && CanManageWindowsFeatures();
        if (RevertGameBoosterSessionButton != null) RevertGameBoosterSessionButton.IsEnabled = enabled && _canRevertGameBoosterSession;
        if (RefreshGameBoosterButton != null) RefreshGameBoosterButton.IsEnabled = enabled;
        if (ShowAiReportButton != null) ShowAiReportButton.IsEnabled = enabled;
        if (ShowComputerReportButton != null) ShowComputerReportButton.IsEnabled = enabled;
        if (CloseGameBoosterReportButton != null) CloseGameBoosterReportButton.IsEnabled = enabled;
        if (GameBoosterRestorePointCheckBox != null) GameBoosterRestorePointCheckBox.IsEnabled = enabled;
        if (TestLocalAiConnectionButton != null) TestLocalAiConnectionButton.IsEnabled = enabled;
        if (SaveLocalAiSettingsButton != null) SaveLocalAiSettingsButton.IsEnabled = enabled;
        if (AnalyzeAndApplyButton != null) AnalyzeAndApplyButton.IsEnabled = enabled;
        if (RunRustLocalAiAnalysisButton != null) RunRustLocalAiAnalysisButton.IsEnabled = enabled;
        if (LocalAiEndpointTextBox != null) LocalAiEndpointTextBox.IsEnabled = enabled;
        if (LocalAiModelComboBox != null) LocalAiModelComboBox.IsEnabled = enabled;

        if (FitModeComboBox != null) FitModeComboBox.IsEnabled = enabled;
        if (ManualCropCheckBox != null) ManualCropCheckBox.IsEnabled = enabled;

        if (CropXTextBox != null) CropXTextBox.IsEnabled = enabled;
        if (CropYTextBox != null) CropYTextBox.IsEnabled = enabled;
        if (CropWidthTextBox != null) CropWidthTextBox.IsEnabled = enabled;
        if (CropHeightTextBox != null) CropHeightTextBox.IsEnabled = enabled;
    }

    private void SetBusyMessage(string message)
    {
        if (BusyOverlayTextBlock != null)
        {
            BusyOverlayTextBlock.Text = message;
        }
    }
}
