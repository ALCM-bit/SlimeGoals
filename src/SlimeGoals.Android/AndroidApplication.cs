using System.Diagnostics;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Android;

/// <summary>
/// Processo do app: monta o container e migra o banco antes de o Avalonia iniciar.
/// A <c>App</c> não tem construtor sem parâmetros, e <c>AvaloniaAndroidApplication&lt;TApp&gt;</c> exige <c>new()</c>:
/// <see cref="AppConstraintStub"/> só satisfaz a restrição; o builder real é criado em <see cref="CreateAppBuilder"/>.
/// </summary>
[Application]
public class AndroidApplication : AvaloniaAndroidApplication<AndroidApplication.AppConstraintStub>
{
    /// <summary>O container do processo; montado no <see cref="OnCreate"/>, antes de qualquer Activity ou receiver.</summary>
    internal static ServiceProvider? Services { get; private set; }

    /// <summary>Nunca instanciada: existe só para a restrição <c>new()</c> do tipo base.</summary>
    public sealed class AppConstraintStub() : SlimeGoals.App.App(null!);

    protected AndroidApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Armazenamento privado do app; desinstalar apaga os dados.
        var databasePath = Path.Combine(FilesDir!.AbsolutePath, "slimegoals.db");
        try
        {
            var services = AndroidCompositionRoot.Build(this, databasePath);
            using (var connection = services.GetRequiredService<SqliteConnectionFactory>().Open())
                MigrationRunner.Migrate(connection);
            Services = services;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao montar o container ou migrar o banco: {ex}");
            throw;
        }

        base.OnCreate();
    }

    /// <summary>Avisa as telas de que os dados podem ter mudado (ao voltar ao app, por exemplo).</summary>
    internal void NotifyDataChanged() => Services?.GetService<DataChangeNotifier>()?.NotifyChanged();

    protected override AppBuilder CreateAppBuilder()
        => AppBuilder.Configure(() => new SlimeGoals.App.App(Services!)).UseAndroid();
}
