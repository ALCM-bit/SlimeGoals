using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

/// <summary>
/// Slime desenhado em um canvas 200×200; a pose liga uma pseudo-classe que escolhe as partes e a animação.
/// As animações só rodam com a pseudo-classe <c>:live</c>: ligada por até <see cref="LiveDuration"/> depois de entrar
/// na tela, mudar de pose ou reativar a janela, e desligada quando a janela fica oculta/inativa ou o app vai para
/// segundo plano. Sem ela o slime fica parado na pose (sem tiques do relógio de animação, CPU ociosa).
/// </summary>
public partial class SlimeMascot : UserControl
{
    public static readonly StyledProperty<MascotPose> PoseProperty =
        AvaloniaProperty.Register<SlimeMascot, MascotPose>(nameof(Pose), MascotPose.Idle);

    // O Avalonia 12.1.4 não casa nenhum animador com RenderTransform (ITransform), então os estilos animam estas
    // propriedades numéricas e o código as repassa às transformações do desenho.
    public static readonly StyledProperty<double> BodyScaleXProperty =
        AvaloniaProperty.Register<SlimeMascot, double>(nameof(BodyScaleX), 1);

    public static readonly StyledProperty<double> BodyScaleYProperty =
        AvaloniaProperty.Register<SlimeMascot, double>(nameof(BodyScaleY), 1);

    public static readonly StyledProperty<double> EyesScaleYProperty =
        AvaloniaProperty.Register<SlimeMascot, double>(nameof(EyesScaleY), 1);

    public static readonly StyledProperty<double> HopOffsetProperty =
        AvaloniaProperty.Register<SlimeMascot, double>(nameof(HopOffset));

    private static readonly (MascotPose Pose, string PseudoClass, string Name)[] Poses =
    [
        (MascotPose.Sleeping, ":sleeping", "Slime dormindo"),
        (MascotPose.Idle, ":idle", "Slime parado"),
        (MascotPose.Happy, ":happy", "Slime feliz"),
        (MascotPose.Celebrating, ":celebrating", "Slime comemorando"),
    ];

    /// <summary>Quanto tempo a animação roda depois de cada gatilho (entrar na tela, mudar de pose, reativar a janela).</summary>
    public static readonly TimeSpan LiveDuration = TimeSpan.FromSeconds(60);

    private const string LivePseudoClass = ":live";

    // Temporizador de disparo único (sem polling): ao vencer, desliga a animação.
    private readonly DispatcherTimer _liveTimer = new() { Interval = LiveDuration };
    private TopLevel? _topLevel;
    private IActivatableLifetime? _lifetime;
    private bool _inBackground;

    public SlimeMascot()
    {
        InitializeComponent();
        ApplyPose(Pose);
        _liveTimer.Tick += (_, _) => StopLive();
    }

    public MascotPose Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    /// <summary>Escala horizontal do corpo (alvo de animação de estilo).</summary>
    public double BodyScaleX
    {
        get => GetValue(BodyScaleXProperty);
        set => SetValue(BodyScaleXProperty, value);
    }

    /// <summary>Escala vertical do corpo (alvo de animação de estilo).</summary>
    public double BodyScaleY
    {
        get => GetValue(BodyScaleYProperty);
        set => SetValue(BodyScaleYProperty, value);
    }

    /// <summary>Escala vertical dos olhos abertos, usada para piscar (alvo de animação de estilo).</summary>
    public double EyesScaleY
    {
        get => GetValue(EyesScaleYProperty);
        set => SetValue(EyesScaleYProperty, value);
    }

    /// <summary>Deslocamento vertical do pulo, em unidades do desenho (alvo de animação de estilo).</summary>
    public double HopOffset
    {
        get => GetValue(HopOffsetProperty);
        set => SetValue(HopOffsetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PoseProperty)
        {
            ApplyPose(change.GetNewValue<MascotPose>());
            ArmLive();
        }
        else if (change.Property == BodyScaleXProperty)
            ((ScaleTransform)Body.RenderTransform!).ScaleX = change.GetNewValue<double>();
        else if (change.Property == BodyScaleYProperty)
            ((ScaleTransform)Body.RenderTransform!).ScaleY = change.GetNewValue<double>();
        else if (change.Property == EyesScaleYProperty)
            ((ScaleTransform)Eyes.RenderTransform!).ScaleY = change.GetNewValue<double>();
        else if (change.Property == HopOffsetProperty)
            Canvas.SetTop(Hop, change.GetNewValue<double>());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        if (_topLevel is Window window)
        {
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
        }
        // Android: a Activity em segundo plano chega pelo ciclo de vida do app, não pela TopLevel.
        _lifetime = Application.Current?.TryGetFeature<IActivatableLifetime>();
        if (_lifetime is not null)
        {
            _lifetime.Activated += OnLifetimeActivated;
            _lifetime.Deactivated += OnLifetimeDeactivated;
        }
        _inBackground = false;
        ArmLive();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
        if (_topLevel is Window window)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
        }
        if (_lifetime is not null)
        {
            _lifetime.Activated -= OnLifetimeActivated;
            _lifetime.Deactivated -= OnLifetimeDeactivated;
        }
        (_topLevel, _lifetime) = (null, null);
        StopLive();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
            ArmLive();
    }

    private void OnWindowActivated(object? sender, EventArgs e) => ArmLive();

    private void OnWindowDeactivated(object? sender, EventArgs e) => StopLive();

    private void OnLifetimeActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background)
            return;
        _inBackground = false;
        ArmLive();
    }

    private void OnLifetimeDeactivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background)
            return;
        _inBackground = true;
        StopLive();
    }

    /// <summary>Liga a animação por <see cref="LiveDuration"/> se o slime está à vista; senão, desliga.</summary>
    private void ArmLive()
    {
        var visible = _topLevel is { IsVisible: true } && (_topLevel is not Window window || window.IsActive) && !_inBackground;
        if (!visible)
        {
            StopLive();
            return;
        }
        _liveTimer.Stop();
        _liveTimer.Start();
        PseudoClasses.Set(LivePseudoClass, true);
    }

    private void StopLive()
    {
        _liveTimer.Stop();
        PseudoClasses.Set(LivePseudoClass, false);
    }

    private void ApplyPose(MascotPose pose)
    {
        foreach (var entry in Poses)
            PseudoClasses.Set(entry.PseudoClass, entry.Pose == pose);
        AutomationProperties.SetName(this, Poses.First(p => p.Pose == pose).Name);
    }
}
