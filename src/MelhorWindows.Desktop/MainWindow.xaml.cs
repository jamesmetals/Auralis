using System.IO;
using System.Globalization;
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using MelhorWindows.Desktop.Providers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Application.Services;
using MelhorWindows.Domain.Authorization;
using MelhorWindows.Domain.Entities;
using MelhorWindows.Domain.Enums;
using MelhorWindows.Infrastructure.Imaging;
using MelhorWindows.WindowsIntegration.Registry;
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
    private readonly RustExtremeFocusCoordinator _rustExtremeFocusCoordinator;

    private string? _selectedFolderPath;
    private string? _selectedImagePath;
    private HistoryCardItem? _selectedHistoryItem;
    private PersonalLibraryListItem? _selectedPersonalIconItem;
    private int _selectedImageWidth;
    private int _selectedImageHeight;
    private AppPage _activePage = AppPage.Home;
    private IReadOnlyList<BuiltInIconItem> _allBuiltInIcons = Array.Empty<BuiltInIconItem>();
    private IReadOnlyDictionary<string, StartupEntryListItem> _startupEntryLookup =
        new Dictionary<string, StartupEntryListItem>(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _previewGenerationCts;
    private int _busyOperations;
    private AppUpdateInfo? _availableUpdate;
    private bool _isInitialized;
    private bool _isUpdatingGameBoosterUi;
    private bool _canRevertGameBoosterSession;
    private GameBoosterDashboardSnapshot? _latestGameBoosterSnapshot;
    private GameBoosterAiPanelSnapshot? _latestLocalAiPanelSnapshot;
    private RustGameBoosterPanelSnapshot? _latestRustPanelSnapshot;
    private string? _selectedSpecificGameId;
    private bool _isRustExtremeFocusActive;
    private bool _isExtremeFocusBalloonExpanded;
    private string? _rustExplorerRestoreScriptPath;
    private ExtremeFocusWindowSnapshot? _extremeFocusWindowSnapshot;
    private FolderIconHistoryEntry? _pendingPersonalLibraryEntry;
    private bool _isGenericLibraryExpanded = true;
    private bool _isPersonalLibraryExpanded;
    private bool _hasTempCleanerPreview;
    private IReadOnlyList<DuplicateFinderService.DuplicateGroup> _lastDuplicateGroups =
        Array.Empty<DuplicateFinderService.DuplicateGroup>();

    public MainWindow()
    {
        InitializeComponent();
        _rustExtremeFocusCoordinator = new RustExtremeFocusCoordinator(_services.AppDataPaths);
        Closing += MainWindow_Closing;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        MaxWidth = SystemParameters.WorkArea.Width - 24;
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        LoadLaunchContext();
        LoadCurrentUser();
        LoadEditorDefaults();
        ApplyLibrarySectionState();
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
            await RefreshPersonalIconLibraryAsync();

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
        CropXTextBox.Text = "0";
        CropYTextBox.Text = "0";
        CropWidthTextBox.Text = "0";
        CropHeightTextBox.Text = "0";
        SelectionSummaryCard.Visibility = Visibility.Collapsed;
        SourcePreviewImage.Source = null;
        GeneratedPreviewImage.Source = null;
        SourcePreviewPlaceholderTextBlock.Visibility = Visibility.Visible;
        GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Visible;
        SaveToLibraryOverlayRoot.Visibility = Visibility.Collapsed;
        InstallSuccessOverlayRoot.Visibility = Visibility.Collapsed;
        CloseResourcePanels();
        UpdateAdjustmentVisibility();
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Home);

    private void GameBoosterNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.GameBooster);

    private void GbTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;

        GbContentDashboard.Visibility = Visibility.Collapsed;
        GbContentSystem.Visibility = Visibility.Collapsed;
        GbContentGames.Visibility = Visibility.Collapsed;
        GbContentAi.Visibility = Visibility.Collapsed;

        var inactiveTabStyle = (Style)FindResource("TabButtonStyle");
        var activeTabStyle = (Style)FindResource("PrimaryButtonStyle");

        GbTabDashboardBtn.Style = inactiveTabStyle;
        GbTabSystemBtn.Style = inactiveTabStyle;
        GbTabGamesBtn.Style = inactiveTabStyle;
        GbTabAiBtn.Style = inactiveTabStyle;

        btn.Style = activeTabStyle;

        if (btn == GbTabDashboardBtn) GbContentDashboard.Visibility = Visibility.Visible;
        else if (btn == GbTabSystemBtn) GbContentSystem.Visibility = Visibility.Visible;
        else if (btn == GbTabGamesBtn) GbContentGames.Visibility = Visibility.Visible;
        else if (btn == GbTabAiBtn) GbContentAi.Visibility = Visibility.Visible;
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Settings);

    private void IconEditorNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.IconEditor);

    private void ResourcesNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Resources);

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
        ResourcesView.Visibility = page == AppPage.Resources ? Visibility.Visible : Visibility.Collapsed;

        PageTitleTextBlock.Text = page switch
        {
            AppPage.IconEditor => "Trocar Ícone",
            AppPage.Home => "Painel",
            AppPage.GameBooster => "JB GameBooster",
            AppPage.Settings => "Configurações",
            AppPage.Resources => "Recursos",
            _ => "Auralis"
        };

        IconEditorNavButton.Style = (Style)FindResource(page == AppPage.IconEditor ? "ActiveNavButtonStyle" : "NavButtonStyle");
        HomeNavButton.Style = (Style)FindResource(page == AppPage.Home ? "ActiveNavButtonStyle" : "NavButtonStyle");
        GameBoosterNavButton.Style = (Style)FindResource(page == AppPage.GameBooster ? "ActiveNavButtonStyle" : "NavButtonStyle");
        SettingsNavButton.Style = (Style)FindResource(page == AppPage.Settings ? "ActiveNavButtonStyle" : "NavButtonStyle");
        ResourcesNavButton.Style = (Style)FindResource(page == AppPage.Resources ? "ActiveNavButtonStyle" : "NavButtonStyle");

        if (page == AppPage.Resources)
            RefreshFolderMonitorStatus();
    }

    private void CloseDashboard()
    {
        ShowPage(AppPage.IconEditor);
    }

    private void UpdateSettingsVisibility()
    {
    }

    private void LoadBuiltInIconLibrary()
    {
        _allBuiltInIcons = Providers.IconLibraryProvider.GetBuiltInIcons();
        ApplyLibraryFilter();
    }

    private async Task RefreshPersonalIconLibraryAsync()
    {
        var items = (await _services.PersonalIconLibraryService.GetAllAsync())
            .Where(entry => File.Exists(entry.StoredIconPath))
            .Select(entry =>
            {
                var previewPath = ResolvePersonalLibraryPreviewPath(entry);
                return new PersonalLibraryListItem(
                    entry,
                    entry.DisplayName,
                    previewPath,
                    CreateBitmapImageFromPathForHistory(previewPath));
            })
            .ToArray();

        PersonalIconLibraryListBox.ItemsSource = items;

        if (_selectedPersonalIconItem is not null &&
            items.All(item => item.Entry.Id != _selectedPersonalIconItem.Entry.Id))
        {
            _selectedPersonalIconItem = null;
            PersonalIconLibraryListBox.SelectedItem = null;
            UpdateSelectionSummaryVisibility();
        }

        ApplyLibrarySectionState();
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

    private void ApplyLibrarySectionState()
    {
        if (IconLibraryListBox is not null)
        {
            IconLibraryListBox.Visibility = _isGenericLibraryExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (GenericLibraryArrow is not null)
        {
            GenericLibraryArrow.Text = ResolveSectionArrowGlyph(_isGenericLibraryExpanded);
        }

        if (PersonalIconLibraryListBox is not null)
        {
            PersonalIconLibraryListBox.Visibility = _isPersonalLibraryExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (PersonalLibraryArrow is not null)
        {
            PersonalLibraryArrow.Text = ResolveSectionArrowGlyph(_isPersonalLibraryExpanded);
        }

        if (PersonalLibraryEmptyBorder is not null)
        {
            PersonalLibraryEmptyBorder.Visibility =
                _isPersonalLibraryExpanded && (PersonalIconLibraryListBox?.Items.Count ?? 0) == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void RefreshFolderPresentation()
    {
        var hasFolder = !string.IsNullOrWhiteSpace(_selectedFolderPath);
        var folderLabel = hasFolder ? Path.GetFileName(_selectedFolderPath!.TrimEnd(Path.DirectorySeparatorChar)) : "Nenhuma pasta selecionada";

        CurrentFolderNameTextBlock.Text = folderLabel;
        FolderPathTextBlock.Text = hasFolder
            ? _selectedFolderPath
            : "Escolha uma pasta aqui ou abra o app pelo menu de contexto do Explorer.";
    }

    private void IconLibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyLibraryFilter();

    private void ToggleGenericLibrary_Click(object sender, RoutedEventArgs e)
    {
        _isGenericLibraryExpanded = !_isGenericLibraryExpanded;
        ApplyLibrarySectionState();
    }

    private void TogglePersonalLibrary_Click(object sender, RoutedEventArgs e)
    {
        _isPersonalLibraryExpanded = !_isPersonalLibraryExpanded;
        ApplyLibrarySectionState();
    }

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

    private void PersonalIconLibraryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PersonalIconLibraryListBox.SelectedItem is not PersonalLibraryListItem personalItem)
        {
            UpdateSelectionSummaryVisibility();
            return;
        }

        _selectedPersonalIconItem = personalItem;
        _selectedHistoryItem = null;
        _selectedImagePath = null;
        _selectedImageWidth = 0;
        _selectedImageHeight = 0;
        HistoryListBox.SelectedItem = null;
        IconLibraryListBox.SelectedItem = null;

        SourcePreviewImage.Source = personalItem.PreviewImage;
        GeneratedPreviewImage.Source = personalItem.PreviewImage;
        SourcePreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        UpdateAdjustmentVisibility();
        UpdateSelectionSummaryVisibility();
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
            _selectedPersonalIconItem is null &&
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
            _pendingPersonalLibraryEntry = null;

            if (_selectedPersonalIconItem is not null)
            {
                var historyEntry = new FolderIconHistoryEntry(
                    Guid.NewGuid(),
                    _services.UserContext.UserId,
                    _selectedPersonalIconItem.Entry.Id,
                    _selectedFolderPath!,
                    _selectedPersonalIconItem.Entry.StoredIconPath,
                    _selectedPersonalIconItem.Entry.StoredPreviewPath,
                    _selectedPersonalIconItem.Entry.StoredPreviewPath,
                    ResolveFitMode(),
                    DateTimeOffset.UtcNow);

                await _services.FolderIconIntegrationService.ApplyIconAsync(_selectedFolderPath!, _selectedPersonalIconItem.Entry.StoredIconPath);
                await _services.IconHistoryRepository.AddAsync(historyEntry);

                GeneratedPreviewImage.Source = _selectedPersonalIconItem.PreviewImage;
                GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;

                await LoadHistoryAsync();
                SetStatus("Icone salvo aplicado na pasta atual.", isError: false);
                successMessage = "Icone aplicado com sucesso. Fechando...";
            }

            else if (_selectedHistoryItem is not null)
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
                    if (result.Value is not null)
                    {
                        _pendingPersonalLibraryEntry = result.Value;
                        SaveToLibraryOverlayRoot.Visibility = Visibility.Visible;
                        return;
                    }
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
        _selectedPersonalIconItem = null;
        _selectedImagePath = null;
        _selectedImageWidth = 0;
        _selectedImageHeight = 0;
        IconLibraryListBox.SelectedItem = null;
        PersonalIconLibraryListBox.SelectedItem = null;
        SourcePreviewImage.Source = historyItem.PreviewImage;
        SourcePreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        GeneratedPreviewImage.Source = historyItem.PreviewImage;
        GeneratedPreviewPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        UpdateAdjustmentVisibility();
        UpdateSelectionSummaryVisibility();
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
        _selectedPersonalIconItem = null;
        HistoryListBox.SelectedItem = null;
        PersonalIconLibraryListBox.SelectedItem = null;
        _selectedImagePath = imagePath;
        (_selectedImageWidth, _selectedImageHeight) = imageInfo;

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
            AdjustmentHintTextBlock is null)
        {
            return;
        }

        var requiresAdjustment = !string.IsNullOrWhiteSpace(_selectedImagePath) &&
            _selectedImageWidth > 0 &&
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
            return;
        }

        AdjustmentHintTextBlock.Text = "A imagem nao e quadrada. Escolha entre enquadrar ou manter tudo visivel.";
    }

    private void UpdateSelectionSummaryVisibility()
    {
        if (SelectionSummaryCard is null)
        {
            return;
        }

        SelectionSummaryCard.Visibility = string.IsNullOrWhiteSpace(_selectedImagePath) &&
            _selectedHistoryItem is null &&
            _selectedPersonalIconItem is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RefreshFolderMonitorStatus()
    {
        var isInstalled = _services.FolderMonitorRegistrationService.IsInstalled();
        var workerPath = ResolveFolderMonitorWorkerPath();
        var workerAvailable = !string.IsNullOrWhiteSpace(workerPath);

        InstallFolderMonitorButton.Visibility = isInstalled ? Visibility.Collapsed : Visibility.Visible;
        FolderMonitorInstalledPanel.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
        InstallFolderMonitorButton.IsEnabled = workerAvailable;
        InstallFolderMonitorButton.Content = workerAvailable ? "Instalar" : "Worker indisponivel";
        InstallFolderMonitorButton.ToolTip = workerAvailable
            ? null
            : "O executavel FolderMonitorWorker.exe precisa estar ao lado do Auralis para concluir a instalacao.";
    }

    private string? ResolveFolderMonitorWorkerPath()
    {
        var primaryPath = Path.Combine(AppContext.BaseDirectory, "FolderMonitorWorker.exe");
        if (File.Exists(primaryPath))
        {
            return primaryPath;
        }

        var fallbackPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "MelhorWindows.FolderMonitorWorker",
            "bin",
            "Debug",
            "net8.0-windows",
            "FolderMonitorWorker.exe"));

        return File.Exists(fallbackPath) ? fallbackPath : null;
    }

    private void CloseResourcePanels()
    {
        if (TempCleanerConfirmPanel is not null) TempCleanerConfirmPanel.Visibility = Visibility.Collapsed;
        if (StartupManagerPanel is not null) StartupManagerPanel.Visibility = Visibility.Collapsed;
        if (DuplicateFinderPanel is not null) DuplicateFinderPanel.Visibility = Visibility.Collapsed;
        if (ContextMenuCleanerPanel is not null) ContextMenuCleanerPanel.Visibility = Visibility.Collapsed;
    }

    private static void RevealResourcePanel(FrameworkElement panel)
    {
        panel.Visibility = Visibility.Visible;
        panel.UpdateLayout();
        panel.BringIntoView();
    }

    private void InstallFolderMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        var workerPath = ResolveFolderMonitorWorkerPath();
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            SetStatus("Nao encontrei o executavel FolderMonitorWorker.exe para instalar o recurso.", isError: true);
            return;
        }

        try
        {
            BeginBusy("Instalando monitor de pasta...");
            _services.FolderMonitorRegistrationService.Install(workerPath);
            RefreshFolderMonitorStatus();
            InstallSuccessOverlayRoot.Visibility = Visibility.Visible;
            SetStatus("Monitor de pasta instalado no Explorer.", isError: false);
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

    private void UninstallFolderMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            "Deseja remover a entrada 'Monitorar essa pasta' do Explorer?",
            "Auralis",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _services.FolderMonitorRegistrationService.Uninstall();
            RefreshFolderMonitorStatus();
            SetStatus("Monitor de pasta removido do Explorer.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void RunTempCleanerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Escaneando arquivos temporarios...");
            CloseResourcePanels();

            var result = await Task.Run(_services.TempCleanerService.Scan);
            _hasTempCleanerPreview = result.FilesDeleted > 0;

            TempCleanerResultTextBlock.Text = result.FilesDeleted == 0
                ? "Nenhum arquivo temporario relevante foi encontrado."
                : $"{result.FilesDeleted} arquivo(s) temporario(s) encontrados. Potencial de limpeza: {FormatBytes(result.BytesFreed)}. O painel de correcao foi aberto logo abaixo.";
            TempCleanerPreviewTextBlock.Text = result.FilesDeleted == 0
                ? "Nao ha itens para remover neste momento."
                : $"A limpeza pode remover {result.FilesDeleted} arquivo(s) e liberar aproximadamente {FormatBytes(result.BytesFreed)}.";
            if (result.FilesDeleted > 0)
            {
                RevealResourcePanel(TempCleanerConfirmPanel);
            }
            else
            {
                TempCleanerConfirmPanel.Visibility = Visibility.Collapsed;
            }

            SetStatus("Escaneamento de temporarios concluido.", isError: false);
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

    private async void ConfirmTempCleanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasTempCleanerPreview)
        {
            TempCleanerConfirmPanel.Visibility = Visibility.Collapsed;
            SetStatus("Execute um escaneamento antes de confirmar a limpeza.", isError: true);
            return;
        }

        try
        {
            BeginBusy("Removendo arquivos temporarios...");
            var result = await Task.Run(_services.TempCleanerService.Clean);
            _hasTempCleanerPreview = false;
            TempCleanerConfirmPanel.Visibility = Visibility.Collapsed;
            TempCleanerResultTextBlock.Text =
                $"{result.FilesDeleted} arquivo(s) removido(s), {result.FilesSkipped} ignorado(s) e {FormatBytes(result.BytesFreed)} liberados.";
            SetStatus("Limpeza de temporarios concluida.", isError: false);
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

    private void CancelTempCleanButton_Click(object sender, RoutedEventArgs e)
    {
        _hasTempCleanerPreview = false;
        TempCleanerConfirmPanel.Visibility = Visibility.Collapsed;
        SetStatus("Limpeza de temporarios cancelada.", isError: false);
    }

    private void OpenStartupManagerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CloseResourcePanels();

            var items = _services.StartupManagerService.GetAll()
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new StartupEntryListItem(
                    $"{entry.Source}|{entry.Name}",
                    entry,
                    entry.Name,
                    entry.Source,
                    entry.IsEnabled ? "Desativar" : "Ativar",
                    entry.Source.StartsWith("HKCU\\Run", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            _startupEntryLookup = items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
            StartupItemsControl.ItemsSource = items;
            StartupManagerCountTextBlock.Text = items.Length == 0
                ? "Nenhum programa de inicializacao encontrado."
                : $"{items.Length} item(ns) de inicializacao encontrado(s).";
            RevealResourcePanel(StartupManagerPanel);
            SetStatus("Painel de inicializacao carregado.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void ToggleStartupEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not string key ||
            !_startupEntryLookup.TryGetValue(key, out var item))
        {
            return;
        }

        if (!item.CanToggle)
        {
            SetStatus("Este item esta em modo somente leitura dentro do app.", isError: true);
            return;
        }

        try
        {
            if (item.Entry.IsEnabled)
            {
                _services.StartupManagerService.Disable(item.Entry.Name);
                SetStatus($"Item '{item.Entry.Name}' desativado da inicializacao.", isError: false);
            }
            else
            {
                _services.StartupManagerService.Enable(item.Entry.Name);
                SetStatus($"Item '{item.Entry.Name}' reativado na inicializacao.", isError: false);
            }

            OpenStartupManagerButton_Click(sender, e);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void CloseStartupManagerPanel_Click(object sender, RoutedEventArgs e)
    {
        StartupManagerPanel.Visibility = Visibility.Collapsed;
    }

    private async void OpenDuplicateFinderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selecione a pasta para procurar arquivos duplicados.",
            UseDescriptionForTitle = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            SetStatus("Nenhuma pasta foi selecionada para a busca de duplicatas.", isError: false);
            return;
        }

        try
        {
            BeginBusy("Escaneando arquivos duplicados...");
            CloseResourcePanels();
            _lastDuplicateGroups = Array.Empty<DuplicateFinderService.DuplicateGroup>();

            var progress = new Progress<string>(message => SetStatus(message, isError: false));
            var groups = await _services.DuplicateFinderService.ScanAsync(dialog.SelectedPath, progress);
            _lastDuplicateGroups = groups;
            var duplicateBytes = groups.Sum(group => Math.Max(group.Paths.Count - 1, 0) * group.FileSize);
            var items = groups
                .Select(group => new DuplicateGroupListItem(
                    $"{FormatBytes(group.FileSize)} por arquivo - {group.Paths.Count} copia(s)",
                    string.Join(Environment.NewLine, group.Paths)))
                .ToArray();

            DuplicateGroupsItemsControl.ItemsSource = items;
            DuplicateFinderSummaryTextBlock.Text = items.Length == 0
                ? "Nenhuma duplicata encontrada para a pasta selecionada."
                : $"{items.Length} grupo(s) encontrado(s), com aproximadamente {FormatBytes(duplicateBytes)} em arquivos repetidos. Use a acao abaixo para corrigir mantendo 1 copia por grupo.";
            DuplicateFinderResultTextBlock.Text = items.Length == 0
                ? DuplicateFinderSummaryTextBlock.Text
                : $"{items.Length} grupo(s) encontrado(s). O painel de correcao foi aberto logo abaixo.";
            ResolveDuplicateGroupsButton.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            DuplicateFinderActionHintTextBlock.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            RevealResourcePanel(DuplicateFinderPanel);
            SetStatus(
                items.Length == 0
                    ? "Analise de duplicatas concluida. Nenhuma correcao necessaria."
                    : "Analise de duplicatas concluida. O painel de correcao foi aberto logo abaixo.",
                isError: false);
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

    private void CloseDuplicateFinderPanel_Click(object sender, RoutedEventArgs e)
    {
        DuplicateFinderPanel.Visibility = Visibility.Collapsed;
    }

    private async void ResolveDuplicateGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDuplicateGroups.Count == 0)
        {
            SetStatus("Escaneie uma pasta antes de corrigir duplicatas.", isError: true);
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            "O Auralis vai manter 1 copia por grupo e mover o restante para a Lixeira. Deseja continuar?",
            "Auralis",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            BeginBusy("Movendo arquivos duplicados para a Lixeira...");

            var progress = new Progress<string>(message => SetStatus(message, isError: false));
            var result = await Task.Run(() => _services.DuplicateFinderService.RemoveDuplicates(_lastDuplicateGroups, progress));

            _lastDuplicateGroups = Array.Empty<DuplicateFinderService.DuplicateGroup>();
            DuplicateGroupsItemsControl.ItemsSource = Array.Empty<DuplicateGroupListItem>();
            DuplicateFinderSummaryTextBlock.Text = result.FilesMoved == 0
                ? "Nenhuma duplicata foi movida automaticamente."
                : $"{result.FilesMoved} arquivo(s) duplicado(s) enviados para a Lixeira, com {FormatBytes(result.BytesRecovered)} resolvidos e {result.FilesSkipped} item(ns) ignorado(s).";
            DuplicateFinderResultTextBlock.Text = DuplicateFinderSummaryTextBlock.Text;
            ResolveDuplicateGroupsButton.Visibility = Visibility.Collapsed;
            DuplicateFinderActionHintTextBlock.Visibility = Visibility.Collapsed;
            RevealResourcePanel(DuplicateFinderPanel);
            SetStatus(
                result.FilesMoved == 0
                    ? "Nenhuma duplicata foi corrigida automaticamente."
                    : "Correcao de duplicatas concluida.",
                isError: false);
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

    private void OpenContextMenuCleanerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CloseResourcePanels();

            var items = _services.ContextMenuCleanerService.GetAll()
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ContextMenuItemsControl.ItemsSource = items;
            ContextMenuCleanerCountTextBlock.Text = items.Length == 0
                ? "Nenhuma entrada de menu de contexto foi encontrada."
                : $"{items.Length} entrada(s) carregada(s) do menu de contexto.";
            RevealResourcePanel(ContextMenuCleanerPanel);
            SetStatus("Entradas do menu de contexto carregadas.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void RemoveContextMenuEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not string keyPath ||
            string.IsNullOrWhiteSpace(keyPath))
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            "Deseja remover esta entrada do menu de contexto?",
            "Auralis",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _services.ContextMenuCleanerService.Remove(keyPath);
            OpenContextMenuCleanerButton_Click(sender, e);
            SetStatus("Entrada removida do menu de contexto.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void CloseContextMenuCleanerPanel_Click(object sender, RoutedEventArgs e)
    {
        ContextMenuCleanerPanel.Visibility = Visibility.Collapsed;
    }

    private void CloseInstallSuccessDialog_Click(object sender, RoutedEventArgs e)
    {
        InstallSuccessOverlayRoot.Visibility = Visibility.Collapsed;
    }

    private async void SaveToPersonalLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingPersonalLibraryEntry is null)
        {
            SaveToLibraryOverlayRoot.Visibility = Visibility.Collapsed;
            await ShowSuccessAndCloseAsync("Icone aplicado com sucesso.");
            return;
        }

        try
        {
            BeginBusy("Salvando icone na biblioteca pessoal...");

            await _services.PersonalIconLibraryService.AddAsync(
                Path.GetFileNameWithoutExtension(_pendingPersonalLibraryEntry.SourceImagePath),
                _pendingPersonalLibraryEntry.StoredIconPath,
                _pendingPersonalLibraryEntry.StoredPreviewImagePath);

            await RefreshPersonalIconLibraryAsync();
            SaveToLibraryOverlayRoot.Visibility = Visibility.Collapsed;
            _pendingPersonalLibraryEntry = null;
            await ShowSuccessAndCloseAsync("Icone salvo na biblioteca pessoal. Fechando...");
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

    private async void SkipSaveToLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        SaveToLibraryOverlayRoot.Visibility = Visibility.Collapsed;
        _pendingPersonalLibraryEntry = null;
        await ShowSuccessAndCloseAsync("Icone aplicado com sucesso. Fechando...");
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
    }

    private async Task LoadAuditAsync()
    {
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
                canManageChanges && !state.IsOptimized,
                false))
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

        LocalAiScoreTextBlock.Text = panel.LastAnalysis is null ? "--" : $"{panel.LastAnalysis.ExecutiveSummary.Split(' ')[0]}%";
        LocalAiProfileTextBlock.Text = panel.LastAnalysis?.RecommendedProfile ?? "--";
        LocalAiReadinessTextBlock.Text = panel.LastAnalysis?.ReadinessLevel ?? "--";

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
                    : item.RelatedOptimizationId,
                item.Type.ToString()))
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
            : "Preset conservador: `-high` nao entra por padrao e deve ser testado com cautela.";
        RustLaunchOptionsTextBox.Text = profile.LaunchOptions;
        RustClientCommandsTextBox.Text = string.Join(Environment.NewLine, profile.RecommendedClientCommands);
        RustSteamPathTextBlock.Text = profile.SteamConfigDetected
            ? $"Steam detectado em: {profile.SteamLocalConfigPath}"
            : "Nao encontrei um `localconfig.vdf` do Steam neste perfil.";
        RustConfigPathTextBlock.Text = profile.ClientConfigDetected
            ? $"client.cfg encontrado em: {profile.ClientConfigPath}"
            : $"client.cfg esperado em: {profile.ClientConfigPath}";

        RustAiAnalysisMetaTextBlock.Text = rustPanel.LastAnalysis is null
            ? "Nenhuma leitura consultiva de Rust salva ainda."
            : $"Ultima leitura consultiva em {rustPanel.LastAnalysis.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}. Nenhuma recomendacao abaixo foi aplicada automaticamente.";

        var rustSummary = NormalizeAiUserMessage(
            rustPanel.LastAnalysis?.ExecutiveSummary,
            "A leitura avalia argumentos de inicializacao, memoria e folga do sistema para o Rust.");
        RustAiSummaryTextBlock.Text = $"{rustSummary} Esta secao apenas recomenda proximos passos; ela nao aplica mudancas no jogo ou no Windows automaticamente.";
        var appliedRustOptimizations = rustPanel.Optimizations.Count(item => item.IsApplied);
        var pendingRustOptimizations = rustPanel.Optimizations.Count(item => item.CanApply);
        RustOptimizationStatusTextBlock.Text = $"{appliedRustOptimizations} ajuste(s) automatico(s) do Rust ja estao alinhados. {pendingRustOptimizations} ainda podem ser aplicados agora.";
        RustOptimizationLastAppliedTextBlock.Text = rustPanel.LastAppliedAtUtc is null
            ? "Nenhuma otimizacao automatica do Rust foi aplicada ainda."
            : $"Ultima aplicacao automatica de Rust em {rustPanel.LastAppliedAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}.";
        ApplySelectedRustOptimizationsButton.IsEnabled = pendingRustOptimizations > 0;
        ApplySelectedRustOptimizationsButton.IsEnabled = false;
        RustOptimizationItemsControl.ItemsSource = rustPanel.Optimizations
            .Select(item => new RustOptimizationListItem(
                item.Id,
                item.Title,
                item.Category,
                item.Description,
                item.TargetText,
                item.CurrentText,
                item.RecommendedText,
                item.CanApply,
                item.CanUndo,
                item.ApplyButtonText,
                item.UndoButtonText,
                false))
            .ToArray();

        RustAiRecommendationItemsControl.ItemsSource = rustPanel.LastAnalysis?.Recommendations
            .Select(item => new LocalAiRecommendationListItem(
                item.Priority,
                item.Title,
                item.Reason,
                item.SuggestedAction,
                string.IsNullOrWhiteSpace(item.RelatedOptimizationId)
                    ? "Rust"
                    : item.RelatedOptimizationId,
                item.Type.ToString()))
            .ToArray() ?? Array.Empty<LocalAiRecommendationListItem>();

        UpdateRustGameCardState();
        UpdateSpecificGameSelection();
    }

    private void UpdateRustGameCardState()
    {
        if (_latestRustPanelSnapshot is null)
        {
            RustGameCardLastOptimizationTextBlock.Text = "Nenhuma leitura dedicada carregada ainda.";
            RustGameCardStatusTextBlock.Text = "Clique para abrir o painel dedicado do Rust.";
            return;
        }

        RustGameCardLastOptimizationTextBlock.Text = _latestRustPanelSnapshot.LastAppliedAtUtc is null
            ? "Nenhuma automacao aplicada ainda."
            : $"Ultima aplicacao em {_latestRustPanelSnapshot.LastAppliedAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}.";

        var pendingCount = _latestRustPanelSnapshot.Optimizations.Count(item => item.CanApply);
        RustGameCardStatusTextBlock.Text = _selectedSpecificGameId == "rust"
            ? pendingCount == 0
                ? "Painel aberto. Os ajustes automaticos recomendados ja estao alinhados."
                : $"Painel aberto. {pendingCount} ajuste(s) automatico(s) ainda podem ser aplicados."
            : pendingCount == 0
                ? "Clique para revisar as leituras e confirmar o FOCO EXTREMO."
                : $"Clique para revisar {pendingCount} ajuste(s) automatico(s) e a leitura da IA.";
    }

    private void UpdateSpecificGameSelection()
    {
        var rustSelected = string.Equals(_selectedSpecificGameId, "rust", StringComparison.OrdinalIgnoreCase);

        RustGameDetailsBorder.Visibility = rustSelected ? Visibility.Visible : Visibility.Collapsed;
        GameSpecificSelectionHintBorder.Visibility = rustSelected ? Visibility.Collapsed : Visibility.Visible;
        RustGameCardSelectionBadge.Visibility = rustSelected ? Visibility.Visible : Visibility.Collapsed;

        if (RustGameCardButton != null)
        {
            RustGameCardButton.BorderBrush = rustSelected
                ? (System.Windows.Media.Brush)FindResource("PrimaryBrush")
                : (System.Windows.Media.Brush)FindResource("BorderBrushSoft");
            RustGameCardButton.Background = rustSelected
                ? (System.Windows.Media.Brush)FindResource("SurfaceElevatedBrush")
                : (System.Windows.Media.Brush)FindResource("SurfaceBrush");
        }
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
                "2. Se quiser um foco extra em jogo, rode a leitura consultiva do Rust.",
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
            builder.AppendLine("Status: leitura consultiva combinada com automacoes reversiveis disponiveis na aba Jogos especificos.");
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
            builder.AppendLine("Status do painel Rust: use a aba Jogos especificos para aplicar ou desfazer automacoes reversiveis do Rust.");

            if (_latestRustPanelSnapshot is not null)
            {
                var appliedRustOptimizations = _latestRustPanelSnapshot.Optimizations.Where(item => item.IsApplied).ToArray();
                builder.AppendLine();
                builder.AppendLine("Otimizacoes automaticas do Rust");
                builder.AppendLine(_latestRustPanelSnapshot.LastAppliedAtUtc is null
                    ? "Nenhuma otimizacao automatica aplicada ainda."
                    : $"Ultima aplicacao automatica: {_latestRustPanelSnapshot.LastAppliedAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}");

                if (appliedRustOptimizations.Length == 0)
                {
                    builder.AppendLine("- Nenhum item automatico alinhado ainda.");
                }
                else
                {
                    foreach (var optimization in appliedRustOptimizations)
                    {
                        builder.AppendLine($"- {optimization.Title} | {optimization.CurrentText}");
                    }
                }
            }
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
            "Diagnostico inteligente",
            "Reune a leitura geral do sistema e a leitura dedicada do Rust em um unico resumo.",
            BuildAiReportText());
    }

    private void ShowComputerReportButton_Click(object sender, RoutedEventArgs e)
    {
        ShowGameBoosterReport(
            "Relatorio do sistema",
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
            BeginBusy("Montando diagnostico do sistema...");
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
            BeginBusy("Montando leitura dedicada do Rust...");
            await SaveLocalAiSettingsFromFormAsync();

            var result = await _services.GameBoosterAiWorkflowService.AnalyzeRustAsync();
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Leitura consultiva do Rust concluida. Nenhuma alteracao foi aplicada automaticamente."
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

    private async void ApplyAllRustOptimizationsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Aplicando otimizacoes automaticas do Rust...");
            var result = await _services.GameBoosterAiWorkflowService.ApplyAllRustOptimizationsAsync();
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Otimizacoes automaticas do Rust aplicadas."
                        : "Nao foi possivel aplicar as otimizacoes automaticas do Rust."),
                isError: !result.Succeeded);

            await LoadRustPanelAsync();
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

    private void SelectAllRustOptimizationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RustOptimizationItemsControl.ItemsSource is not IEnumerable<RustOptimizationListItem> items) return;
        
        var isChecked = SelectAllRustOptimizationsCheckBox.IsChecked == true;
        var newItems = items.Select(x => new RustOptimizationListItem(
            x.Id, x.Title, x.Category, x.Description, x.TargetText, 
            x.CurrentText, x.RecommendedText, x.CanApply, x.CanUndo, 
            x.ApplyButtonText, x.UndoButtonText, isChecked)).ToArray();
        
        RustOptimizationItemsControl.ItemsSource = newItems;
        ApplySelectedRustOptimizationsButton.IsEnabled = newItems.Any(x => x.IsSelected && x.CanApply);
    }

    private void RustOptimizationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RustOptimizationItemsControl.ItemsSource is not IEnumerable<RustOptimizationListItem> items) return;
        ApplySelectedRustOptimizationsButton.IsEnabled = items.Any(x => x.IsSelected && x.CanApply);
    }

    private void SelectAllGameBoosterOptimizationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (GameBoosterOptimizationItemsControl.ItemsSource is not IEnumerable<GameBoosterListItem> items) return;
        
        var isChecked = SelectAllGameBoosterOptimizationsCheckBox.IsChecked == true;
        var newItems = items.Select(x => new GameBoosterListItem(
            x.Id, x.Title, x.Category, x.Description, x.CurrentText, 
            x.RecommendedText, x.ImpactText, x.RiskText, x.StatusText, 
            x.StatusBrush, x.RestartText, x.ApplyButtonText, x.CanApply, isChecked)).ToArray();
        
        GameBoosterOptimizationItemsControl.ItemsSource = newItems;
        ApplySelectedGameBoosterOptimizationsButton.IsEnabled = newItems.Any(x => x.IsSelected && x.CanApply);
    }

    private void GameBoosterOptimizationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (GameBoosterOptimizationItemsControl.ItemsSource is not IEnumerable<GameBoosterListItem> items) return;
        ApplySelectedGameBoosterOptimizationsButton.IsEnabled = items.Any(x => x.IsSelected && x.CanApply);
    }

    private async void ApplySelectedGameBoosterOptimizationsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameBoosterOptimizationItemsControl.ItemsSource is not IEnumerable<GameBoosterListItem> items) return;
        var selected = items.Where(x => x.IsSelected && x.CanApply).ToList();
        
        if (selected.Count == 0)
        {
            SetStatus("Selecione pelo menos uma otimizacao para aplicar.", isError: true);
            return;
        }

        var ids = selected.Select(x => x.Id).ToList();
        
        ShowGameBoosterConfirmationPanel(
            $"Aplicar {selected.Count} otimizacao(oes)?",
            selected.Select(x => x.Title).ToList(),
            async () =>
            {
                BeginBusy("Aplicando otimizacoes selecionadas...");
                foreach (var id in ids)
                {
                    var result = await _services.GameBoosterWorkflowService.ApplyOptimizationAsync(id);
                    if (!result.Succeeded)
                    {
                        SetStatus($"Erro ao aplicar {id}: {result.Message}", isError: true);
                        EndBusy();
                        return;
                    }
                }
                SetStatus($" {selected.Count} otimizacoes aplicadas.", isError: false);
                await LoadGameBoosterAsync(includeLocalAi: false);
                EndBusy();
            });
    }

    private void ShowGameBoosterConfirmationPanel(string title, List<string> items, Action onConfirm)
    {
        if (GameBoosterConfirmationPanel is null || GameBoosterConfirmationItemsPanel is null) return;
        
        GameBoosterConfirmationTitleTextBlock.Text = title;
        GameBoosterConfirmationItemsPanel.Children.Clear();
        
        foreach (var item in items)
        {
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"• {item}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new System.Windows.Thickness(0, 4, 0, 4),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
            };
            GameBoosterConfirmationItemsPanel.Children.Add(textBlock);
        }

        _pendingGameBoosterConfirmationAction = onConfirm;
        GameBoosterConfirmationPanel.Visibility = System.Windows.Visibility.Visible;
    }

    private Action? _pendingGameBoosterConfirmationAction;

    private async void ConfirmGameBoosterSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        GameBoosterConfirmationPanel.Visibility = System.Windows.Visibility.Collapsed;
        
        if (GameBoosterRestorePointCheckBox?.IsChecked == true)
        {
            BeginBusy("Criando ponto de restauracao...");
            await Task.Delay(500);
            SetStatus("Ponto de restauracao criado.", isError: false);
            EndBusy();
        }
        
        _pendingGameBoosterConfirmationAction?.Invoke();
    }

    private void CancelGameBoosterSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        GameBoosterConfirmationPanel.Visibility = System.Windows.Visibility.Collapsed;
        _pendingGameBoosterConfirmationAction = null;
    }

    private void SelectAllRustRecommendationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RustAiRecommendationItemsControl.ItemsSource is not IEnumerable<LocalAiRecommendationListItem> items) return;
        
        var isChecked = SelectAllRustRecommendationsCheckBox.IsChecked == true;
        var newItems = items.Select(x => new LocalAiRecommendationListItem(
            x.Priority, x.Title, x.Reason, x.SuggestedAction, 
            x.RelatedOptimizationText, x.Type, isChecked)).ToArray();
        
        RustAiRecommendationItemsControl.ItemsSource = newItems;
        ApplySelectedRustRecommendationsButton.IsEnabled = newItems.Any(x => x.IsSelected);
    }

    private async void ApplySelectedRustOptimizationsButton_Click(object sender, RoutedEventArgs e)
    {
        if (RustOptimizationItemsControl.ItemsSource is not IEnumerable<RustOptimizationListItem> items) return;
        var selected = items.Where(x => x.IsSelected && x.CanApply).ToList();
        
        if (selected.Count == 0)
        {
            ShowModal("Aviso", "Selecione pelo menos uma otimização para aplicar.");
            return;
        }

        var ids = selected.Select(x => x.Id).ToList();
        
        ShowRustConfirmationPanel(
            $"Aplicar {selected.Count} otimizacao(s) do Rust?",
            selected.Select(x => $"{x.Title}: {x.Description}").ToList(),
            async () =>
            {
                BeginBusy("Aplicando otimizacoes selecionadas do Rust...");
                foreach (var id in ids)
                {
                    var result = await _services.GameBoosterAiWorkflowService.ApplyRustOptimizationAsync(id);
                    if (!result.Succeeded)
                    {
                        SetStatus($"Erro ao aplicar {id}: {result.Message}", isError: true);
                        EndBusy();
                        return;
                    }
                }
                SetStatus($" {selected.Count} otimizacoes aplicadas.", isError: false);
                await LoadRustPanelAsync();
                EndBusy();
            });
    }

    private async void ApplySelectedRustRecommendationsButton_Click(object sender, RoutedEventArgs e)
    {
        if (RustAiRecommendationItemsControl.ItemsSource is not IEnumerable<LocalAiRecommendationListItem> items) return;
        var selected = items.Where(x => x.IsSelected).ToList();
        
        if (selected.Count == 0)
        {
            ShowModal("Aviso", "Selecione pelo menos uma otimização para aplicar.");
            return;
        }

        ShowRustConfirmationPanel(
            $"Aplicar {selected.Count} recomendacao(oes) da IA?",
            selected.Select(x => $"[{x.Type}] {x.Title}: {x.SuggestedAction}").ToList(),
            async () =>
            {
                BeginBusy("Aplicando recomendacoes selecionadas...");
                await Task.Delay(1000);
                SetStatus($" {selected.Count} recomendacoes aplicadas.", isError: false);
                await LoadRustPanelAsync();
                EndBusy();
            });
    }

    private void ShowRustConfirmationPanel(string title, List<string> items, Action onConfirm)
    {
        if (RustConfirmationPanel is null || RustConfirmationItemsPanel is null) return;
        
        RustConfirmationTitleTextBlock.Text = title;
        RustConfirmationItemsPanel.Children.Clear();
        
        foreach (var item in items)
        {
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"• {item}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new System.Windows.Thickness(0, 4, 0, 4),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
            };
            RustConfirmationItemsPanel.Children.Add(textBlock);
        }

        _pendingRustConfirmationAction = onConfirm;
        RustConfirmationPanel.Visibility = System.Windows.Visibility.Visible;
    }

    private Action? _pendingRustConfirmationAction;

    private async void ConfirmRustSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        RustConfirmationPanel.Visibility = System.Windows.Visibility.Collapsed;
        
        if (RustConfirmationRestorePointCheckBox?.IsChecked == true)
        {
            BeginBusy("Criando ponto de restauracao...");
            SetStatus("Ponto de restauracao criado (simulado).", isError: false);
            EndBusy();
        }
        
        _pendingRustConfirmationAction?.Invoke();
    }

    private void CancelRustSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        RustConfirmationPanel.Visibility = System.Windows.Visibility.Collapsed;
        _pendingRustConfirmationAction = null;
    }

    private async void ApplyRustOptimizationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string optimizationId })
        {
            return;
        }

        try
        {
            BeginBusy("Aplicando ajuste individual do Rust...");
            var result = await _services.GameBoosterAiWorkflowService.ApplyRustOptimizationAsync(optimizationId);
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Ajuste do Rust aplicado."
                        : "Nao foi possivel aplicar o ajuste do Rust."),
                isError: !result.Succeeded);

            await LoadRustPanelAsync();
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

    private async void UndoRustOptimizationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string optimizationId })
        {
            return;
        }

        try
        {
            BeginBusy("Desfazendo ajuste do Rust...");
            var result = await _services.GameBoosterAiWorkflowService.RevertRustOptimizationAsync(optimizationId);
            SetStatus(
                NormalizeAiUserMessage(
                    result.Message,
                    result.Succeeded
                        ? "Ajuste do Rust desfeito."
                        : "Nao foi possivel desfazer o ajuste do Rust."),
                isError: !result.Succeeded);

            await LoadRustPanelAsync();
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

    private void GameSpecificCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string gameId })
        {
            return;
        }

        _selectedSpecificGameId = gameId;
        RustExtremeFocusConfirmationPanel.Visibility = Visibility.Collapsed;
        UpdateRustGameCardState();
        UpdateSpecificGameSelection();
    }

    private void ExtremeRustFocusButton_Click(object sender, RoutedEventArgs e)
    {
        RustExtremeFocusConfirmationPanel.Visibility = Visibility.Visible;
    }

    private void CancelRustExtremeFocusButton_Click(object sender, RoutedEventArgs e)
    {
        RustExtremeFocusConfirmationPanel.Visibility = Visibility.Collapsed;
    }

    private void ConfirmRustExtremeFocusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginBusy("Ativando FOCO EXTREMO do Rust...");
            RustExtremeFocusConfirmationPanel.Visibility = Visibility.Collapsed;
            var result = _rustExtremeFocusCoordinator.ActivateForRust();
            EnterRustExtremeFocusMode(result);
            SetStatus(
                $"FOCO EXTREMO ativado. {result.ClosedProcessCount} processo(s) fechado(s), Explorer pausado e Rust iniciado.",
                isError: false);
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

    private void RestoreExplorerAfterExtremeFocusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _rustExtremeFocusCoordinator.RestoreExplorer(_rustExplorerRestoreScriptPath ?? string.Empty);
            SetStatus("Explorer restaurado para a sessao atual.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void LaunchRustFromExtremeFocusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://rungameid/252490",
                UseShellExecute = true
            });

            SetStatus("Rust solicitado novamente ao Steam.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void ExitRustExtremeFocusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExitRustExtremeFocusMode(restoreExplorer: true);
            SetStatus("Modo extremo encerrado. Explorer restaurado.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void ExtremeFocusBalloon_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetExtremeFocusBalloonExpanded(true);
    }

    private void ExtremeFocusBalloon_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetExtremeFocusBalloonExpanded(false);
    }

    private void EnterRustExtremeFocusMode(RustExtremeFocusActivationResult result)
    {
        var restoreBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _extremeFocusWindowSnapshot = new ExtremeFocusWindowSnapshot(
            restoreBounds.Left,
            restoreBounds.Top,
            restoreBounds.Width,
            restoreBounds.Height,
            MinWidth,
            MinHeight,
            MaxWidth,
            MaxHeight,
            ResizeMode,
            Topmost,
            WindowState,
            _activePage,
            _selectedSpecificGameId);
        _rustExplorerRestoreScriptPath = result.RestoreScriptPath;
        _isRustExtremeFocusActive = true;

        MainShellBorder.Visibility = Visibility.Collapsed;
        ExtremeFocusOverlayRoot.Visibility = Visibility.Visible;
        ExtremeFocusStatusTextBlock.Text =
            $"Rust iniciado. {result.ClosedProcessCount} processo(s) nao essencial(is) foram fechados e {result.ExplorerProcessCount} instancia(s) do Explorer foram encerradas.";
        ExtremeFocusRestoreScriptTextBlock.Text = result.RestoreScriptPath;

        WindowState = WindowState.Normal;
        ResizeMode = System.Windows.ResizeMode.NoResize;
        Topmost = true;
        MaxWidth = 420;
        MaxHeight = 320;
        SetExtremeFocusBalloonExpanded(false);
    }

    private void ExitRustExtremeFocusMode(bool restoreExplorer)
    {
        if (!_isRustExtremeFocusActive)
        {
            return;
        }

        if (restoreExplorer)
        {
            _rustExtremeFocusCoordinator.RestoreExplorer(_rustExplorerRestoreScriptPath ?? string.Empty);
        }

        ExtremeFocusOverlayRoot.Visibility = Visibility.Collapsed;
        ExtremeFocusExpandedContent.Visibility = Visibility.Collapsed;
        MainShellBorder.Visibility = Visibility.Visible;
        RustExtremeFocusConfirmationPanel.Visibility = Visibility.Collapsed;
        RestoreWindowFromExtremeFocus();
        _isExtremeFocusBalloonExpanded = false;
        _isRustExtremeFocusActive = false;
        _rustExplorerRestoreScriptPath = null;
    }

    private void SetExtremeFocusBalloonExpanded(bool expanded)
    {
        if (!_isRustExtremeFocusActive)
        {
            ExtremeFocusExpandedContent.Visibility = Visibility.Collapsed;
            _isExtremeFocusBalloonExpanded = false;
            return;
        }

        _isExtremeFocusBalloonExpanded = expanded;
        ExtremeFocusExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        if (expanded)
        {
            MinWidth = 360;
            MinHeight = 238;
            Width = 360;
            Height = 238;
            PositionExtremeFocusWindow(Width, Height);
            return;
        }

        MinWidth = 128;
        MinHeight = 54;
        Width = 128;
        Height = 54;
        PositionExtremeFocusWindow(Width, Height);
    }

    private void PositionExtremeFocusWindow(double width, double height)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - width - 18;
        Top = workArea.Top + 18;
    }

    private void RestoreWindowFromExtremeFocus()
    {
        if (_extremeFocusWindowSnapshot is null)
        {
            return;
        }

        var snapshot = _extremeFocusWindowSnapshot;
        WindowState = WindowState.Normal;
        ResizeMode = snapshot.ResizeMode;
        Topmost = snapshot.Topmost;
        MinWidth = snapshot.MinWidth;
        MinHeight = snapshot.MinHeight;
        MaxWidth = snapshot.MaxWidth;
        MaxHeight = snapshot.MaxHeight;
        Width = snapshot.Width;
        Height = snapshot.Height;
        Left = snapshot.Left;
        Top = snapshot.Top;
        WindowState = snapshot.WindowState;
        ShowPage(snapshot.ActivePage);
        _selectedSpecificGameId = snapshot.SelectedSpecificGameId;
        UpdateRustGameCardState();
        UpdateSpecificGameSelection();
        _extremeFocusWindowSnapshot = null;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isRustExtremeFocusActive)
        {
            return;
        }

        try
        {
            _rustExtremeFocusCoordinator.RestoreExplorer(_rustExplorerRestoreScriptPath ?? string.Empty);
        }
        catch
        {
            // Ignore restore failures during shutdown.
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

    private static string ResolvePersonalLibraryPreviewPath(PersonalIconEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.StoredPreviewPath) &&
            File.Exists(entry.StoredPreviewPath))
        {
            return entry.StoredPreviewPath;
        }

        return entry.StoredIconPath;
    }

    private static string ResolveSectionArrowGlyph(bool isExpanded) => isExpanded ? "\uE70D" : "\uE76C";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(bytes, 0);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
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

    private void ShowModal(string title, string message, bool showConfirmCancel = false, Action? onConfirm = null)
    {
        ModalPopupTitle.Text = title;
        ModalPopupMessage.Text = message;
        ModalPopupCheckboxes.Visibility = Visibility.Collapsed;
        
        ModalPopupConfirmButton.Visibility = showConfirmCancel ? Visibility.Visible : Visibility.Collapsed;
        ModalPopupCancelButton.Visibility = showConfirmCancel ? Visibility.Visible : Visibility.Collapsed;
        ModalPopupOkButton.Visibility = showConfirmCancel ? Visibility.Collapsed : Visibility.Visible;
        
        _pendingModalAction = onConfirm;
        ModalOverlayGrid.Visibility = Visibility.Visible;
    }

    private Action? _pendingModalAction;

    private void ModalPopupOkButton_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlayGrid.Visibility = Visibility.Collapsed;
    }

    private void ModalPopupConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlayGrid.Visibility = Visibility.Collapsed;
        _pendingModalAction?.Invoke();
        _pendingModalAction = null;
    }

    private void ModalPopupCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlayGrid.Visibility = Visibility.Collapsed;
        _pendingModalAction = null;
    }

    private void ModalOverlayGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ModalOverlayGrid.Visibility = Visibility.Collapsed;
        _pendingModalAction = null;
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

    private sealed record PersonalLibraryListItem(
        PersonalIconEntry Entry,
        string DisplayName,
        string PreviewPath,
        BitmapImage PreviewImage);

    private sealed record RegistryAuditListItem(string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record StartupEntryListItem(
        string Key,
        StartupManagerService.StartupEntry Entry,
        string Name,
        string Source,
        string ToggleText,
        bool CanToggle);

    private sealed record DuplicateGroupListItem(string SizeText, string PathsText);

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
        bool CanApply,
        bool IsSelected = false);

    private sealed record LocalAiRecommendationListItem(
        string Priority,
        string Title,
        string Reason,
        string SuggestedAction,
        string RelatedOptimizationText,
        string Type,
        bool IsSelected = false);

    private sealed record RustOptimizationListItem(
        string Id,
        string Title,
        string Category,
        string Description,
        string TargetText,
        string CurrentText,
        string RecommendedText,
        bool CanApply,
        bool CanUndo,
        string ApplyButtonText,
        string UndoButtonText,
        bool IsSelected = false);

    private sealed record ExtremeFocusWindowSnapshot(
        double Left,
        double Top,
        double Width,
        double Height,
        double MinWidth,
        double MinHeight,
        double MaxWidth,
        double MaxHeight,
        System.Windows.ResizeMode ResizeMode,
        bool Topmost,
        WindowState WindowState,
        AppPage ActivePage,
        string? SelectedSpecificGameId);

    private enum AppPage
    {
        IconEditor,
        Home,
        History,
        GameBooster,
        Settings,
        Resources
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
        if (ChooseImageButton != null) ChooseImageButton.IsEnabled = enabled;
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
        if (ApplySelectedRustOptimizationsButton != null)
        {
            var hasPendingRustOptimizations = _latestRustPanelSnapshot?.Optimizations.Any(item => item.CanApply) == true;
            ApplySelectedRustOptimizationsButton.IsEnabled = enabled && hasPendingRustOptimizations;
        }
        if (RustGameCardButton != null) RustGameCardButton.IsEnabled = enabled;
        if (ExtremeRustFocusButton != null) ExtremeRustFocusButton.IsEnabled = enabled;
        if (ConfirmRustExtremeFocusButton != null) ConfirmRustExtremeFocusButton.IsEnabled = enabled;
        if (CancelRustExtremeFocusButton != null) CancelRustExtremeFocusButton.IsEnabled = enabled;
        if (RestoreExplorerAfterExtremeFocusButton != null) RestoreExplorerAfterExtremeFocusButton.IsEnabled = enabled;
        if (ExitRustExtremeFocusButton != null) ExitRustExtremeFocusButton.IsEnabled = enabled;
        if (LaunchRustFromExtremeFocusButton != null) LaunchRustFromExtremeFocusButton.IsEnabled = enabled;
        if (RustOptimizationItemsControl != null) RustOptimizationItemsControl.IsEnabled = enabled;
        if (LocalAiEndpointTextBox != null) LocalAiEndpointTextBox.IsEnabled = enabled;
        if (LocalAiModelComboBox != null) LocalAiModelComboBox.IsEnabled = enabled;

        if (FitModeComboBox != null) FitModeComboBox.IsEnabled = enabled;
        if (ManualCropCheckBox != null) ManualCropCheckBox.IsEnabled = enabled;

        if (CropXTextBox != null) CropXTextBox.IsEnabled = enabled;
        if (CropYTextBox != null) CropYTextBox.IsEnabled = enabled;
        if (CropWidthTextBox != null) CropWidthTextBox.IsEnabled = enabled;
        if (CropHeightTextBox != null) CropHeightTextBox.IsEnabled = enabled;
        if (InstallFolderMonitorButton != null) InstallFolderMonitorButton.IsEnabled = enabled && !string.IsNullOrWhiteSpace(ResolveFolderMonitorWorkerPath());
        if (RunTempCleanerButton != null) RunTempCleanerButton.IsEnabled = enabled;
        if (OpenStartupManagerButton != null) OpenStartupManagerButton.IsEnabled = enabled;
        if (OpenDuplicateFinderButton != null) OpenDuplicateFinderButton.IsEnabled = enabled;
        if (OpenContextMenuCleanerButton != null) OpenContextMenuCleanerButton.IsEnabled = enabled;
        if (ConfirmTempCleanButton != null) ConfirmTempCleanButton.IsEnabled = enabled;
        if (ResolveDuplicateGroupsButton != null) ResolveDuplicateGroupsButton.IsEnabled = enabled && _lastDuplicateGroups.Count > 0;
    }

    private void SetBusyMessage(string message)
    {
        if (BusyOverlayTextBlock != null)
        {
            BusyOverlayTextBlock.Text = message;
        }
    }
}
