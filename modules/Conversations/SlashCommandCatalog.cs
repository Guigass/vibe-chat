namespace VibeChat.Conversations;

/// <summary>Localized slash descriptions (B-100). Names and usage stay stable.</summary>
public static class SlashCommandCatalog
{
    private static readonly Dictionary<string, Dictionary<string, string>> Descriptions = new(StringComparer.Ordinal)
    {
        ["dm"] = Copy(
            "Abre ou cria uma DM",
            "Open or create a DM",
            "Abre o crea un MD",
            "Ouvre ou crée une DM",
            "Öffnet oder erstellt eine DM",
            "Apre o crea un DM",
            "DMを開くまたは作成する",
            "打开或创建私信",
            "DM을 열거나 만듭니다",
            "Открыть или создать личные сообщения"),
        ["topico"] = Copy(
            "Altera a descrição do canal",
            "Change the channel description",
            "Cambia la descripción del canal",
            "Modifie la description du canal",
            "Ändert die Kanalbeschreibung",
            "Modifica la descrizione del canale",
            "チャンネルの説明を変更する",
            "更改频道说明",
            "채널 설명을 변경합니다",
            "Изменить описание канала"),
        ["convidar"] = Copy(
            "Convida alguém para o workspace",
            "Invite someone to the workspace",
            "Invita a alguien al workspace",
            "Invite quelqu'un dans l'espace de travail",
            "Lädt jemanden in den Workspace ein",
            "Invita qualcuno nel workspace",
            "ワークスペースに招待する",
            "邀请他人加入工作区",
            "워크스페이스에 초대합니다",
            "Пригласить кого-то в рабочее пространство"),
        ["resumir"] = Copy(
            "Resume as mensagens recentes do canal",
            "Summarize recent channel messages",
            "Resume los mensajes recientes del canal",
            "Résume les messages récents du canal",
            "Fasst die letzten Nachrichten des Kanals zusammen",
            "Riassume i messaggi recenti del canale",
            "チャンネルの最近のメッセージを要約する",
            "总结频道最近的消息",
            "채널의 최근 메시지를 요약합니다",
            "Суммировать недавние сообщения канала"),
        ["apagar"] = Copy(
            "Apaga a sua última mensagem neste canal",
            "Delete your last message in this channel",
            "Elimina tu último mensaje en este canal",
            "Supprime votre dernier message dans ce canal",
            "Löscht deine letzte Nachricht in diesem Kanal",
            "Elimina il tuo ultimo messaggio in questo canale",
            "このチャンネルの自分の最後のメッセージを削除する",
            "删除你在此频道的最后一条消息",
            "이 채널에서 내 마지막 메시지를 삭제합니다",
            "Удалить ваше последнее сообщение в этом канале"),
        ["enquete"] = Copy(
            "Cria uma enquete no canal",
            "Create a poll in the channel",
            "Crea una encuesta en el canal",
            "Crée un sondage dans le canal",
            "Erstellt eine Umfrage im Kanal",
            "Crea un sondaggio nel canale",
            "チャンネルにアンケートを作成する",
            "在频道中创建投票",
            "채널에 투표를 만듭니다",
            "Создать опрос в канале"),
        ["ajuda"] = Copy(
            "Lista os comandos disponíveis",
            "List available commands",
            "Lista los comandos disponibles",
            "Liste les commandes disponibles",
            "Listet die verfügbaren Befehle auf",
            "Elenca i comandi disponibili",
            "利用可能なコマンドを一覧表示する",
            "列出可用命令",
            "사용 가능한 명령을 나열합니다",
            "Показать доступные команды"),
    };

    public static string Describe(string name, string? locale)
    {
        if (!Descriptions.TryGetValue(name, out var byLocale))
        {
            return string.Empty;
        }

        if (locale is not null && byLocale.TryGetValue(locale, out var text))
        {
            return text;
        }

        return byLocale["pt-BR"];
    }

    private static Dictionary<string, string> Copy(
        string ptBr,
        string en,
        string es,
        string fr,
        string de,
        string it,
        string ja,
        string zhCn,
        string ko,
        string ru) =>
        new(StringComparer.Ordinal)
        {
            ["pt-BR"] = ptBr,
            ["en"] = en,
            ["es"] = es,
            ["fr"] = fr,
            ["de"] = de,
            ["it"] = it,
            ["ja"] = ja,
            ["zh-CN"] = zhCn,
            ["ko"] = ko,
            ["ru"] = ru,
        };
}
