using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Authorization;
using MelhorWindows.Domain.Entities;
using MelhorWindows.Domain.Enums;
using Microsoft.Win32;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace MelhorWindows.Desktop;

public partial class MainWindow : Window
{
    private const string UnsupportedImageMessage = "Nao foi possivel abrir essa imagem. Use SVG, PNG, JPG, JPEG, BMP, ICO, WEBP, GIF ou TIFF.";
    private const string SupportedImageDialogFilter = "Image Files|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.webp;*.gif;*.tif;*.tiff|All Files|*.*";
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg",
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".ico",
        ".webp",
        ".gif",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> ImageSharpPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".webp",
        ".gif",
        ".tif",
        ".tiff"
    };
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
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await HandleStartupCommandsAsync();
            LoadBuiltInIconLibrary();
            UpdateSettingsVisibility();
            await LoadHistoryAsync();

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
            ? "Este perfil pode acessar integracao com Explorer, recursos administrativos do Windows e auditoria."
            : "Este perfil ve apenas configuracoes relevantes ao uso diario. Recursos administrativos ficam escondidos.";
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

    private void HistoryNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.History);

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Settings);

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
        DashboardOverlayRoot.Visibility = Visibility.Visible;
        HomeView.Visibility = page == AppPage.Home ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = page == AppPage.History ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource(page == AppPage.Home ? "ActiveNavButtonStyle" : "NavButtonStyle");
        HistoryNavButton.Style = (Style)FindResource(page == AppPage.History ? "ActiveNavButtonStyle" : "NavButtonStyle");
        SettingsNavButton.Style = (Style)FindResource(page == AppPage.Settings ? "ActiveNavButtonStyle" : "NavButtonStyle");
    }

    private void CloseDashboard()
    {
        DashboardOverlayRoot.Visibility = Visibility.Collapsed;
        _activePage = AppPage.Home;
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
        _allBuiltInIcons =
        [
            new BuiltInIconItem("folder", "Folder", "\uE8B7", CreateBrush("#7A6CFF")),
            new BuiltInIconItem("spark", "Spark", "\uE945", CreateBrush("#49A8FF")),
            new BuiltInIconItem("rocket", "Rocket", "\uE7C3", CreateBrush("#FF7A4F")),
            new BuiltInIconItem("database", "Database", "\uE9D2", CreateBrush("#33C5A5")),
            new BuiltInIconItem("diamond", "Diamond", "\uECAD", CreateBrush("#E5A300")),
            new BuiltInIconItem("star", "Star", "\uE734", CreateBrush("#7084FF")),
            new BuiltInIconItem("heart", "Heart", "\uEB51", CreateBrush("#E84D7A")),
            new BuiltInIconItem("gear", "Settings", "\uE713", CreateBrush("#5D89FF")),
            new BuiltInIconItem("mail", "Mail", "\uE715", CreateBrush("#5A9DFF")),
            new BuiltInIconItem("edit", "Edit", "\uE70F", CreateBrush("#31A2A2")),
            new BuiltInIconItem("doc", "Document", "\uE130", CreateBrush("#5B7BD5")),
            new BuiltInIconItem("calendar", "Calendar", "\uE787", CreateBrush("#876BFF")),
            new BuiltInIconItem("bell", "Bell", "\uE7F4", CreateBrush("#497CFB")),
            new BuiltInIconItem("person", "Person", "\uE77B", CreateBrush("#5B82D6")),
            new BuiltInIconItem("image", "Image", "\uE91B", CreateBrush("#41BFA1")),
            new BuiltInIconItem("lock", "Lock", "\uE72E", CreateBrush("#516C9A"))
        ];

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

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
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
            SetStatus("Pasta alvo atualizada.", isError: false);
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

        try
        {
            BeginBusy();
            SetStatus("Aplicando ícone...", isError: false);

            if (_selectedHistoryItem is not null)
            {
                await _services.FolderIconIntegrationService.ApplyIconAsync(_selectedFolderPath, _selectedHistoryItem.Entry.StoredIconPath);
                SetStatus("Ícone recente aplicado na pasta atual.", isError: false);
                return;
            }

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

        try
        {
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
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
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

    private async Task LoadSelectedImageAsync(string imagePath, bool refreshPreview)
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

        if (refreshPreview)
        {
            await UpdatePreviewAsync();
        }
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

        BeginBusy();
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
            Filter = SupportedImageDialogFilter,
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
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedImageExtensions.Contains(extension);
    }

    private string EnsureBuiltInIconImage(BuiltInIconItem iconItem)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "MelhorWindows", "LibraryIcons");
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
            if (IsSvgPath(imagePath))
            {
                return ReadSvgInfo(imagePath);
            }

            return ShouldUseImageSharpPreviewPipeline(imagePath)
                ? ReadImageInfoWithImageSharp(imagePath)
                : ReadImageInfoWithWpfDecoder(imagePath);
        }
        catch (Exception exception) when (IsUnsupportedImageException(exception))
        {
            throw new InvalidOperationException(UnsupportedImageMessage, exception);
        }
    }

    private static BitmapImage CreateBitmapImageFromPath(string imagePath, int decodePixelWidth)
    {
        try
        {
            if (IsSvgPath(imagePath))
            {
                return CreateBitmapImageFromSvgPath(imagePath, decodePixelWidth);
            }

            return ShouldUseImageSharpPreviewPipeline(imagePath)
                ? CreateBitmapImageFromPathWithImageSharp(imagePath, decodePixelWidth)
                : CreateBitmapImageFromPathWithWpfDecoder(imagePath, decodePixelWidth);
        }
        catch (Exception exception) when (IsUnsupportedImageException(exception))
        {
            throw new InvalidOperationException(UnsupportedImageMessage, exception);
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

    private static (int Width, int Height) ReadSvgInfo(string imagePath)
    {
        var drawing = LoadSvgDrawing(imagePath);
        var bounds = ResolveDrawingBounds(drawing);
        return ((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
    }

    private static BitmapImage CreateBitmapImageFromSvgPath(string imagePath, int decodePixelWidth)
    {
        var drawing = LoadSvgDrawing(imagePath);
        var bounds = ResolveDrawingBounds(drawing);
        var (pixelWidth, pixelHeight) = ResolveSvgRenderSize(bounds, decodePixelWidth);
        var pngBytes = RenderDrawingToPngBytes(drawing, bounds, pixelWidth, pixelHeight);
        return CreateBitmapImageFromBytes(pngBytes);
    }

    private static (int Width, int Height) ReadImageInfoWithImageSharp(string imagePath)
    {
        using var stream = OpenImageReadStream(imagePath);
        var info = ImageSharpImage.Identify(stream);

        if (info is null)
        {
            throw new InvalidOperationException(UnsupportedImageMessage);
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
            throw new InvalidOperationException(UnsupportedImageMessage);
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

    private static bool IsSvgPath(string imagePath) =>
        string.Equals(Path.GetExtension(imagePath), ".svg", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUseImageSharpPreviewPipeline(string imagePath)
    {
        var extension = Path.GetExtension(imagePath);
        return !string.IsNullOrWhiteSpace(extension) && ImageSharpPreviewExtensions.Contains(extension);
    }

    private static DrawingGroup LoadSvgDrawing(string imagePath)
    {
        var settings = new WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = false,
            EnsureViewboxPosition = true,
            EnsureViewboxSize = true
        };

        using var reader = new FileSvgReader(settings);
        var drawing = reader.Read(imagePath);

        if (drawing is null)
        {
            throw new InvalidOperationException(UnsupportedImageMessage);
        }

        if (drawing.CanFreeze)
        {
            drawing.Freeze();
        }

        return drawing;
    }

    private static Rect ResolveDrawingBounds(DrawingGroup drawing)
    {
        var bounds = drawing.Bounds;

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new Rect(0, 0, 256, 256);
        }

        return bounds;
    }

    private static (int PixelWidth, int PixelHeight) ResolveSvgRenderSize(Rect bounds, int decodePixelWidth)
    {
        var width = Math.Max(1d, bounds.Width);
        var height = Math.Max(1d, bounds.Height);

        if (decodePixelWidth > 0)
        {
            var scale = Math.Min(decodePixelWidth / width, decodePixelWidth / height);

            if (scale > 0)
            {
                width *= scale;
                height *= scale;
            }
        }

        return ((int)Math.Ceiling(width), (int)Math.Ceiling(height));
    }

    private static byte[] RenderDrawingToPngBytes(DrawingGroup drawing, Rect bounds, int pixelWidth, int pixelHeight)
    {
        var brush = new DrawingBrush(drawing)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.Uniform,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds
        };

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, pixelWidth, pixelHeight));
            context.DrawRectangle(brush, null, new Rect(0, 0, pixelWidth, pixelHeight));
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool IsUnsupportedImageException(Exception exception) =>
        exception is
            NotSupportedException or
            ArgumentException or
            OutOfMemoryException or
            System.Runtime.InteropServices.COMException or
            System.Xml.XmlException or
            UnknownImageFormatException or
            InvalidImageContentException;

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

    static MainWindow()
    {
        // Reaproveita instâncias para reduzir alocações e micro-travamentos.
        StatusSuccessBackgroundBrush.Freeze();
        StatusSuccessBorderBrush.Freeze();
        StatusSuccessTextBrush.Freeze();
        StatusErrorBackgroundBrush.Freeze();
        StatusErrorBorderBrush.Freeze();
        StatusErrorTextBrush.Freeze();
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

    private sealed record BuiltInIconItem(
        string Id,
        string Label,
        string Glyph,
        System.Windows.Media.Brush TileBackgroundBrush);

    private sealed record RegistryAuditListItem(string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private enum AppPage
    {
        Home,
        History,
        Settings
    }

    private void BeginBusy()
    {
        _busyOperations++;
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

        if (ApplyIconButton != null) ApplyIconButton.IsEnabled = enabled;
        if (UpdatePreviewButton != null) UpdatePreviewButton.IsEnabled = enabled;
        if (ChooseImageButton != null) ChooseImageButton.IsEnabled = enabled;
        if (ReplaceImageButton != null) ReplaceImageButton.IsEnabled = enabled;

        if (FitModeComboBox != null) FitModeComboBox.IsEnabled = enabled;
        if (ManualCropCheckBox != null) ManualCropCheckBox.IsEnabled = enabled;

        if (CropXTextBox != null) CropXTextBox.IsEnabled = enabled;
        if (CropYTextBox != null) CropYTextBox.IsEnabled = enabled;
        if (CropWidthTextBox != null) CropWidthTextBox.IsEnabled = enabled;
        if (CropHeightTextBox != null) CropHeightTextBox.IsEnabled = enabled;
    }
}
