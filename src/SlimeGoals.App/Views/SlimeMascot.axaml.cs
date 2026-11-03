using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

/// <summary>Slime desenhado em um canvas 200×200; a pose liga uma pseudo-classe que escolhe as partes e a animação.</summary>
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

    public SlimeMascot()
    {
        InitializeComponent();
        ApplyPose(Pose);
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
            ApplyPose(change.GetNewValue<MascotPose>());
        else if (change.Property == BodyScaleXProperty)
            ((ScaleTransform)Body.RenderTransform!).ScaleX = change.GetNewValue<double>();
        else if (change.Property == BodyScaleYProperty)
            ((ScaleTransform)Body.RenderTransform!).ScaleY = change.GetNewValue<double>();
        else if (change.Property == EyesScaleYProperty)
            ((ScaleTransform)Eyes.RenderTransform!).ScaleY = change.GetNewValue<double>();
        else if (change.Property == HopOffsetProperty)
            Canvas.SetTop(Hop, change.GetNewValue<double>());
    }

    private void ApplyPose(MascotPose pose)
    {
        foreach (var entry in Poses)
            PseudoClasses.Set(entry.PseudoClass, entry.Pose == pose);
        AutomationProperties.SetName(this, Poses.First(p => p.Pose == pose).Name);
    }
}
