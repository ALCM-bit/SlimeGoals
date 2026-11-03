using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
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
    private ServiceProvider? _services;

    /// <summary>Nunca instanciada: existe só para a restrição <c>new()</c> do tipo base.</summary>
    public sealed class AppConstraintStub() : SlimeGoals.App.App(null!);

    protected AndroidApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Armazenamento privado do app; desinstalar apaga os dados.
        var databasePath = Path.Combine(FilesDir!.AbsolutePath, "slimegoals.db");
        _services = AndroidCompositionRoot.Build(databasePath);
        using (var connection = _services.GetRequiredService<SqliteConnectionFactory>().Open())
            MigrationRunner.Migrate(connection);

        base.OnCreate();
    }

    protected override AppBuilder CreateAppBuilder()
        => AppBuilder.Configure(() => new SlimeGoals.App.App(_services!)).UseAndroid();
}
