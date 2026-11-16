namespace SlimeGoals.Core.Abstractions;

/// <summary>Qual aviso de fim de bloco mostrar, decidido pelo bloco seguinte: pausa, foco ou nenhum.</summary>
public enum FocusAlertKind
{
    /// <summary>"Hora da pausa".</summary>
    BreakTime,

    /// <summary>"Hora de voltar ao foco".</summary>
    FocusTime,

    /// <summary>"Sessão concluída".</summary>
    SessionDone,
}

/// <summary>
/// Porta de saída do aviso de fim de bloco, com som de alarme. Há no máximo um aviso à mostra: mostrar outro substitui
/// o anterior. Chamado de qualquer thread; retirar ou silenciar sem aviso à mostra não faz nada.
/// </summary>
public interface IFocusAlert
{
    /// <summary>Mostra o aviso, com som. Os textos estão em <see cref="FocusAlertText"/>.</summary>
    void Show(FocusAlertKind kind);

    /// <summary>Para o som e deixa o aviso na central.</summary>
    void Silence();

    /// <summary>Retira o aviso à mostra.</summary>
    void Withdraw();
}

/// <summary>Os textos dos avisos de fim de bloco.</summary>
public static class FocusAlertText
{
    public static string Of(FocusAlertKind kind) => kind switch
    {
        FocusAlertKind.BreakTime => "Hora da pausa",
        FocusAlertKind.FocusTime => "Hora de voltar ao foco",
        FocusAlertKind.SessionDone => "Sessão concluída",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
