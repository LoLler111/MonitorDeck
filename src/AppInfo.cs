// Monitor Deck - informazioni sulla versione.
// Per una nuova release: aggiorna Version qui, aggiungi la sezione in CHANGELOG.md,
// poi crea e invia il tag "v" + Version (vedi README).

using System.Reflection;

[assembly: AssemblyTitle("Monitor Deck")]
[assembly: AssemblyProduct("Monitor Deck")]
[assembly: AssemblyDescription("Controllo DDC/CI dei monitor")]
[assembly: AssemblyCopyright("LoLler111")]
[assembly: AssemblyVersion(MonitorDeck.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(MonitorDeck.AppInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(MonitorDeck.AppInfo.Version)]

namespace MonitorDeck
{
    static class AppInfo
    {
        public const string Name = "Monitor Deck";
        public const string Version = "2.0.0";
        public const string RepoOwner = "LoLler111";
        public const string RepoName = "MonitorDeck";
        public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
    }
}
