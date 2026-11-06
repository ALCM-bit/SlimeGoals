using System.Diagnostics;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Android;

/// <summary>
/// Processo do app: monta o container e migra o banco antes de o Avalonia iniciar. Se falhar, o processo não cai:
/// <see cref="Services"/> fica nulo, a tela vira a de erro e os receivers terminam sem fazer nada.
/// A <c>App</c> não tem construtor sem parâmetros, e <c>AvaloniaAndroidApplication&lt;TApp&gt;</c> exige <c>new()</c>:
/// <see cref="AppConstraintStub"/> só satisfaz a restrição; o builder real é criado em <see cref="CreateAppBuilder"/>.
/// </summary>
[Application]
public class AndroidApplication : AvaloniaAndroidApplication<AndroidApplication.AppConstraintStub>
{
    /// <summary>
    /// O container do processo; montado no <see cref="OnCreate"/>, antes de qualquer Activity ou receiver.
    /// Nulo se o banco não abriu.
    /// </summary>
    internal static ServiceProvider? Services { get; private set; }

    /// <summary>Container só com a tela de erro; existe quando o banco não abriu.</summary>
    private static ServiceProvider? _failureServices;

    /// <summary>A tela de erro, quando o banco não abriu; a <see cref="MainActivity"/> atende o pedido de fechar.</summary>
    internal static StartupFailureViewModel? StartupFailure => _failureServices?.GetService<StartupFailureViewModel>();

    /// <summary>Nunca instanciada: existe só para a restrição <c>new()</c> do tipo base.</summary>
    public sealed class AppConstraintStub() : SlimeGoals.App.App(null!);

    protected AndroidApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Armazenamento privado do app; desinstalar apaga os dados.
        var databasePath = Path.Combine(FilesDir!.AbsolutePath, "slimegoals.db");
        ServiceProvider? services = null;
        try
        {
            services = AndroidCompositionRoot.Build(this, databasePath);
            using (var connection = services.GetRequiredService<SqliteConnectionFactory>().Open())
                MigrationRunner.Migrate(connection);
            Services = services;
        }
        catch (Exception ex)
        {
            // Relançar derrubaria o processo em toda abertura e todo broadcast. O arquivo fica como está.
            Trace.TraceError($"Falha ao montar o container ou migrar o banco em {databasePath}: {ex}");
            services?.Dispose();
            _failureServices = StartupFailureServices.Create(databasePath);
        }

        base.OnCreate();
    }

    /// <summary>Avisa as telas de que os dados podem ter mudado (ao voltar ao app, por exemplo).</summary>
    internal void NotifyDataChanged() => Services?.GetService<DataChangeNotifier>()?.NotifyChanged();

    protected override AppBuilder CreateAppBuilder()
        => AppBuilder.Configure(() => new SlimeGoals.App.App((Services ?? _failureServices)!)).UseAndroid();
}
