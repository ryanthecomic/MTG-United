using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// Interface language: English (the source), Português (Brasil), Español.
    /// Every label goes through <see cref="T"/> when its text is set (SymbolText), so the code keeps writing
    /// English and the table below supplies the translation. Lookups ignore case (an ALL-CAPS source gets an
    /// ALL-CAPS translation), keep leading/trailing punctuation, keep the "\tKey" hint of menu items, and fall
    /// back to translating line by line. Text that isn't in the table (card names, rules text, Forge's own
    /// prompts, player names) is left exactly as it is.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Codes = { "en", "pt", "es" };
        public static readonly string[] Names = { "ENGLISH", "PORTUGUÊS (BR)", "ESPAÑOL" };

        static int lang = -1;
        /// <summary>0 = English, 1 = Português (Brasil), 2 = Español.</summary>
        public static int Language
        {
            get
            {
                if (lang < 0)
                {
                    string saved = PlayerPrefs.GetString("language", "");
                    lang = Array.IndexOf(Codes, saved);
                    if (lang < 0)
                        lang = Application.systemLanguage == SystemLanguage.Portuguese ? 1
                             : Application.systemLanguage == SystemLanguage.Spanish ? 2 : 0;
                }
                return lang;
            }
            set
            {
                int v = Mathf.Clamp(value, 0, Codes.Length - 1);
                if (v == Language) return;
                lang = v;
                PlayerPrefs.SetString("language", Codes[v]);
                cache.Clear();
                SymbolText.RelocalizeAll();
                Changed?.Invoke();
            }
        }

        public static event Action Changed;

        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        static Dictionary<string, string[]> table;

        /// <summary>The text in the current language (unchanged if there is no translation).</summary>
        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || Language == 0) return s;
            if (cache.TryGetValue(s, out var hit)) return hit;
            string r = Translate(s, 0) ?? s;
            if (cache.Count > 4000) cache.Clear();
            cache[s] = r;
            return r;
        }

        static readonly Regex Edges = new Regex(@"^([\s!•·…\-–—\(]*)(.*?)([\s\.…:!?\)]*)$", RegexOptions.Singleline);

        static string Translate(string s, int depth)
        {
            if (depth > 3 || s.Length > 600) return null;
            EnsureTable();
            // Menu items: "Draw\tC" — translate the label, keep the key hint.
            int tab = s.IndexOf('\t');
            if (tab >= 0)
            {
                string left = Translate(s.Substring(0, tab), depth + 1);
                return left == null ? null : left + s.Substring(tab);
            }
            string direct = Lookup(s);
            if (direct != null) return direct;
            var m = Edges.Match(s);
            if (m.Success && m.Groups[2].Length > 0 && m.Groups[2].Length < s.Length)
            {
                string core = Lookup(m.Groups[2].Value);
                if (core != null) return m.Groups[1].Value + core + m.Groups[3].Value;
            }
            foreach (var (re, pt, es) in Templates)
            {
                var tm = re.Match(s);
                if (!tm.Success) continue;
                string fmt = Language == 1 ? pt : es;
                for (int g = tm.Groups.Count - 1; g >= 1; g--)
                    fmt = fmt.Replace("{" + g + "}", T(tm.Groups[g].Value));
                return fmt;
            }
            if (s.IndexOf('\n') >= 0)
            {
                var lines = s.Split('\n');
                bool any = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string tl = lines[i].Length == 0 ? null : Translate(lines[i], depth + 1);
                    if (tl != null) { lines[i] = tl; any = true; }
                }
                return any ? string.Join("\n", lines) : null;
            }
            return null;
        }

        static string Lookup(string s)
        {
            if (!table.TryGetValue(s.ToLowerInvariant(), out var row)) return null;
            string t = row[Language - 1];
            if (string.IsNullOrEmpty(t)) return null;
            bool caps = s.ToUpperInvariant() == s && s.ToLowerInvariant() != s;
            return caps ? t.ToUpperInvariant() : t;
        }

        static readonly (Regex re, string pt, string es)[] Templates =
        {
            (new Regex(@"^(\d+) players$"), "{1} jogadores", "{1} jugadores"),
            (new Regex(@"^Table · (\d+) players$"), "Mesa · {1} jogadores", "Mesa · {1} jugadores"),
            (new Regex(@"^OPPONENTS +(\d+)$"), "OPONENTES   {1}", "OPONENTES   {1}"),
            (new Regex(@"^OTHER SEATS +(\d+)$"), "OUTROS LUGARES   {1}", "OTROS ASIENTOS   {1}"),
            (new Regex(@"^AI OPPONENTS +(\d+)$"), "OPONENTES IA   {1}", "OPONENTES IA   {1}"),
            (new Regex(@"^Turn (\d+) · (.+)$"), "Turno {1} · {2}", "Turno {1} · {2}"),
            (new Regex(@"^(.+)'s turn$"), "Turno de {1}", "Turno de {1}"),
            (new Regex(@"^(.+)'s table$"), "Mesa de {1}", "Mesa de {1}"),
            (new Regex(@"^(\d+) cards$"), "{1} cartas", "{1} cartas"),
            (new Regex(@"^Seat (\d+)$"), "Lugar {1}", "Asiento {1}"),
            (new Regex(@"^Player (\d+)$"), "Jogador {1}", "Jugador {1}"),
            (new Regex(@"^Library \((\d+)\)$"), "Grimório ({1})", "Biblioteca ({1})"),
            (new Regex(@"^Graveyard \((\d+)\)$"), "Cemitério ({1})", "Cementerio ({1})"),
            (new Regex(@"^Exile \((\d+)\)$"), "Exílio ({1})", "Exilio ({1})"),
            (new Regex(@"^Command zone \((\d+)\)$"), "Zona de comando ({1})", "Zona de mando ({1})"),
            (new Regex(@"^Revealed this game \((\d+)\)$"), "Revelados nesta partida ({1})", "Reveladas en esta partida ({1})"),
            (new Regex(@"^You can\x27t play (.+) right now\.$"), "Você não pode jogar {1} agora.", "No puedes jugar {1} ahora."),
            (new Regex(@"^Imported (.+) \((.+)\): (\d+) cards, (\d+) images \((\d+) without an image: they\x27ll show as text cards\)\.$"), "Importado {1} ({2}): {3} cartas, {4} imagens ({5} sem imagem: aparecerão como cartas de texto).", "Importado {1} ({2}): {3} cartas, {4} imágenes ({5} sin imagen: se verán como cartas de texto)."),
            (new Regex(@"^Imported (.+) \((.+)\): (\d+) cards, (\d+) images\.$"), "Importado {1} ({2}): {3} cartas, {4} imagens.", "Importado {1} ({2}): {3} cartas, {4} imágenes."),
            (new Regex(@"^(\d+) cards  ·  (\d+) with images  ·  (\d+) tokens$"), "{1} cartas  ·  {2} com imagem  ·  {3} fichas", "{1} cartas  ·  {2} con imagen  ·  {3} fichas"),
            (new Regex(@"^Removed (.+)\.$"), "{1} removido.", "{1} eliminado."),
            (new Regex(@"^Your opened cards \((\d+)\) and basic lands  ·  click to add  ·  build 40 cards$"), "Suas cartas abertas ({1}) e terrenos básicos  ·  clique para adicionar  ·  monte 40 cartas", "Tus cartas abiertas ({1}) y tierras básicas  ·  haz clic para añadir  ·  construye 40 cartas"),
            (new Regex(@"^All (\d+) cards of (.+)  ·  click to add  ·  type to filter$"), "Todas as {1} cartas de {2}  ·  clique para adicionar  ·  digite para filtrar", "Las {1} cartas de {2}  ·  haz clic para añadir  ·  escribe para filtrar"),
            (new Regex(@"^No cards match ""(.*)""$"), "Nenhuma carta corresponde a \"{1}\"", "Ninguna carta coincide con \"{1}\""),
            (new Regex(@"^(\d+) cards \(Scryfall search\)  ·  click or drag to add$"), "{1} cartas (busca do Scryfall)  ·  clique ou arraste para adicionar", "{1} cartas (búsqueda de Scryfall)  ·  haz clic o arrastra para añadir"),
            (new Regex(@"^(\d+) closest matches  ·  click or drag to the list to add  ·  ◆ = in your deck  \(Forge isn\x27t running: only custom sets searched\)$"), "{1} resultados mais próximos  ·  clique ou arraste para a lista para adicionar  ·  ◆ = no seu deck  (o Forge não está rodando: só coleções custom foram buscadas)", "{1} resultados más cercanos  ·  haz clic o arrastra a la lista para añadir  ·  ◆ = en tu mazo  (Forge no está en marcha: solo se buscaron colecciones custom)"),
            (new Regex(@"^(\d+) closest matches  ·  click or drag to the list to add  ·  ◆ = in your deck$"), "{1} resultados mais próximos  ·  clique ou arraste para a lista para adicionar  ·  ◆ = no seu deck", "{1} resultados más cercanos  ·  haz clic o arrastra a la lista para añadir  ·  ◆ = en tu mazo"),
            (new Regex(@"^""(.+)"" has changes that aren\x27t saved yet\.$"), "\"{1}\" tem alterações que ainda não foram salvas.", "\"{1}\" tiene cambios que aún no se han guardado."),
            (new Regex(@"^Saved a copy: (.+)$"), "Cópia salva: {1}", "Copia guardada: {1}"),
            (new Regex(@"^Imported (\d+) cards\. Save to keep them, or close and discard\.$"), "{1} cartas importadas. Salve para mantê-las, ou feche e descarte.", "{1} cartas importadas. Guarda para conservarlas, o cierra y descarta."),
            (new Regex(@"^Imported (\d+) cards\. Type a name for the deck\.$"), "{1} cartas importadas. Digite um nome para o deck.", "{1} cartas importadas. Escribe un nombre para el mazo."),
            (new Regex(@"^(\d+) SETS$"), "{1} COLEÇÕES", "{1} COLECCIONES"),
            (new Regex(@"^(\d+) sets?$"), "{1} coleções", "{1} colecciones"),
            (new Regex(@"^You opened only one (.+)\.$"), "Você abriu só um(a) {1}.", "Solo abriste un(a) {1}."),
            (new Regex(@"^You opened (\d+) (.+)\.$"), "Você abriu {1} {2}.", "Abriste {1} {2}."),
            (new Regex(@"^(.+) is already in the deck \(Commander is singleton\)\.$"), "{1} já está no deck (Commander só permite uma cópia).", "{1} ya está en el mazo (Commander solo permite una copia)."),
            (new Regex(@"^POOL: (.+)$"), "POOL: {1}", "POOL: {1}"),
            (new Regex(@"^Couldn\x27t read the file: (.+)$"), "Não foi possível ler o arquivo: {1}", "No se pudo leer el archivo: {1}"),
            (new Regex(@"^Couldn\x27t export: (.+)$"), "Não foi possível exportar: {1}", "No se pudo exportar: {1}"),
            (new Regex(@"^Exported to (.+)$"), "Exportado para {1}", "Exportado a {1}"),
            (new Regex(@"^Heads up: ""(.+)"" is a (.+) deck, playing it as (.+)\.$"), "Atenção: \"{1}\" é um deck {2}, jogando como {3}.", "Atención: \"{1}\" es un mazo {2}, jugándolo como {3}."),
            (new Regex(@"^Your sets: (.+)  <color=#9aa0aa>\((\d+) cards\)</color>$"), "Suas coleções: {1}  <color=#9aa0aa>({2} cartas)</color>", "Tus colecciones: {1}  <color=#9aa0aa>({2} cartas)</color>"),
            (new Regex(@"^""(.+)"" will be deleted for good\.$"), "\"{1}\" será excluído para sempre.", "\"{1}\" se eliminará para siempre."),
            (new Regex(@"^Deleted ""(.+)""$"), "\"{1}\" excluído", "\"{1}\" eliminado"),
            (new Regex(@"^GAMES  (\d+)        WINS  <color=#f7a540>(\d+)</color>        LOSSES  (\d+)        WIN RATE  (.+)$"), "PARTIDAS  {1}        VITÓRIAS  <color=#f7a540>{2}</color>        DERROTAS  {3}        APROVEITAMENTO  {4}", "PARTIDAS  {1}        VICTORIAS  <color=#f7a540>{2}</color>        DERROTAS  {3}        % DE VICTORIAS  {4}"),
            (new Regex(@"^Deleted (\d+) cached images\. They download again when needed \(restart to clear memory too\)\.$"), "{1} imagens em cache apagadas. Elas são baixadas de novo quando preciso (reinicie para liberar a memória também).", "{1} imágenes en caché eliminadas. Se descargan de nuevo cuando hagan falta (reinicia para liberar también la memoria)."),
            (new Regex(@"^Waiting for Forge \(port (\d+)\)$"), "Esperando o Forge (porta {1})", "Esperando a Forge (puerto {1})"),
            (new Regex(@"^\(\+(\d+) more\)$"), "(+{1} mais)", "(+{1} más)"),
            (new Regex(@"^<color=#f7a540>●</color>  (.+)\.\.\.$"), "<color=#f7a540>●</color>  {1}...", "<color=#f7a540>●</color>  {1}..."),
            (new Regex(@"^Using your own Forge \((.+)\)\.$"), "Usando o seu próprio Forge ({1}).", "Usando tu propio Forge ({1})."),
            (new Regex(@"^Seat (\d+):  (.+)$"), "Lugar {1}:  {2}", "Asiento {1}:  {2}"),
            (new Regex(@"^SEAT (\d+)$"), "LUGAR {1}", "ASIENTO {1}"),
            (new Regex(@"^Winner: (.+)$"), "Vencedor: {1}", "Ganador: {1}"),
            (new Regex(@"^Assigned (\d+) of (\d+)$"), "Atribuído {1} de {2}", "Asignado {1} de {2}"),
            (new Regex(@"^Known in this game \(your deck and cards you\x27ve seen\)\. Type to search all (\d+) names\.$"), "Conhecidas nesta partida (seu deck e cartas que você viu). Digite para buscar entre todos os {1} nomes.", "Conocidas en esta partida (tu mazo y cartas que has visto). Escribe para buscar entre los {1} nombres."),
            (new Regex(@"^Type to search all (\d+) card names\.$"), "Digite para buscar entre os {1} nomes de cartas.", "Escribe para buscar entre los {1} nombres de cartas."),
            (new Regex(@"^No card named like ""(.*)""\.$"), "Nenhuma carta com nome parecido com \"{1}\".", "Ninguna carta con un nombre como \"{1}\"."),
            (new Regex(@"^Pick at least (\d+)\.  Selected: (\d+)$"), "Escolha pelo menos {1}.  Selecionadas: {2}", "Elige al menos {1}.  Seleccionadas: {2}"),
            (new Regex(@"^at least (\d+)$"), "pelo menos {1}", "al menos {1}"),
            (new Regex(@"^Bridge running: (.*)$"), "Ponte rodando: {1}", "Puente en marcha: {1}"),
            (new Regex(@"^Bridge stopped \(exit (-?\d+)\)\. (.*)$"), "Ponte parada (saída {1}). {2}", "Puente detenido (salida {1}). {2}"),
            (new Regex(@"^Click in order \(first = top\)\. Pick (.+)\.  Selected: (\d+)$"), "Clique na ordem (primeira = topo). Escolha {1}.  Selecionadas: {2}", "Haz clic en orden (primera = arriba). Elige {1}.  Seleccionadas: {2}"),
            (new Regex(@"^Pick (.+)\.  Selected: (\d+)$"), "Escolha {1}.  Selecionadas: {2}", "Elige {1}.  Seleccionadas: {2}"),
            (new Regex(@"^COMMANDER   ·   (.+) GOES FIRST$"), "COMMANDER   ·   {1} COMEÇA", "COMMANDER   ·   {1} EMPIEZA"),
            (new Regex(@"^Scryfall has no cards for set ""(.+)""\.$"), "O Scryfall não tem cartas da coleção \"{1}\".", "Scryfall no tiene cartas de la colección \"{1}\"."),
            (new Regex(@"^No cards found for (.+)\.$"), "Nenhuma carta encontrada para {1}.", "No se encontraron cartas para {1}."),
            (new Regex(@"^Cube: (.+)$"), "Cubo: {1}", "Cubo: {1}"),
            (new Regex(@"^Cube from deck: (.+)$"), "Cubo do deck: {1}", "Cubo del mazo: {1}"),
            (new Regex(@"^Cube · (.+)$"), "Cubo · {1}", "Cubo · {1}"),
            (new Regex(@"^Your deck: (.+?)  ·  name: (.+?)  ·  the host picks the format$"), "Seu deck: {1}  ·  nome: {2}  ·  o anfitrião escolhe o formato", "Tu mazo: {1}  ·  nombre: {2}  ·  el anfitrión elige el formato"),
            (new Regex(@"^Your deck: (.+?)  ·  name: (.+?)  ·  format: <color=#f7a540>(.+)</color> \(change it on Home\)$"), "Seu deck: {1}  ·  nome: {2}  ·  formato: <color=#f7a540>{3}</color> (mude no Início)", "Tu mazo: {1}  ·  nombre: {2}  ·  formato: <color=#f7a540>{3}</color> (cámbialo en Inicio)"),
            (new Regex(@"^Your deck: (.+?)  ·  (.+?)  ·  name: (.+)$"), "Seu deck: {1}  ·  {2}  ·  nome: {3}", "Tu mazo: {1}  ·  {2}  ·  nombre: {3}"),
            (new Regex(@"^Your address: <color=#f7a540>(.+)</color>$"), "Seu endereço: <color=#f7a540>{1}</color>", "Tu dirección: <color=#f7a540>{1}</color>"),
            (new Regex(@"^Seated at (.+)\. The host starts the game\.$"), "Sentado em {1}. O anfitrião inicia a partida.", "Sentado en {1}. El anfitrión inicia la partida."),
            (new Regex(@"^Connecting to (.+)\.\.\.$"), "Conectando a {1}...", "Conectando a {1}..."),
            (new Regex(@"^(\d+)\.  (.+)  \(host\)$"), "{1}.  {2}  (anfitrião)", "{1}.  {2}  (anfitrión)"),
            (new Regex(@"^(\d+)\.  AI opponent$"), "{1}.  Oponente IA", "{1}.  Oponente IA"),
            (new Regex(@"^A (.+) deck from Forge$"), "Um deck {1} do Forge", "Un mazo {1} de Forge"),
            (new Regex(@"^Hosting a (.+) table · (\d+) people, (\d+) AI$"), "Hospedando uma mesa de {1} · {2} pessoas, {3} IA", "Organizando una mesa de {1} · {2} personas, {3} IA"),
            (new Regex(@"^(.+)  ·  (\d+) people  ·  in a game$"), "{1}  ·  {2} pessoas  ·  em partida", "{1}  ·  {2} personas  ·  en partida"),
            (new Regex(@"^(.+)  ·  (\d+) people$"), "{1}  ·  {2} pessoas", "{1}  ·  {2} personas"),
            (new Regex(@"^(.+)  ·  (\d+)/(\d+) players  ·  in progress$"), "{1}  ·  {2}/{3} jogadores  ·  em andamento", "{1}  ·  {2}/{3} jugadores  ·  en curso"),
            (new Regex(@"^(.+)  ·  (\d+)/(\d+) players$"), "{1}  ·  {2}/{3} jogadores", "{1}  ·  {2}/{3} jugadores"),
            (new Regex(@"^Couldn\x27t reach the table at (.+)$"), "Não foi possível alcançar a mesa em {1}", "No se pudo alcanzar la mesa en {1}"),
            (new Regex(@"^Can\x27t listen for LAN games: (.+)$"), "Não dá para procurar partidas na LAN: {1}", "No se pueden buscar partidas en LAN: {1}"),
            (new Regex(@"^(.+) is ready: (.+)$"), "{1} está pronto: {2}", "{1} está listo: {2}"),
            (new Regex(@"^(.+) left the table\.$"), "{1} saiu da mesa.", "{1} dejó la mesa."),
            (new Regex(@"^Couldn\x27t open port (\d+): (.+)$"), "Não foi possível abrir a porta {1}: {2}", "No se pudo abrir el puerto {1}: {2}"),
            (new Regex(@"^Hosting on (.+)$"), "Hospedando em {1}", "Organizando en {1}"),
            (new Regex(@"^(.+): done\.$"), "{1}: concluído.", "{1}: listo."),
            (new Regex(@"^(.+) failed\. See the output below\.$"), "{1} falhou. Veja a saída abaixo.", "{1} falló. Mira la salida abajo."),
            (new Regex(@"^<b>Setup:</b>  (.+)$"), "<b>Configuração:</b>  {1}", "<b>Configuración:</b>  {1}"),
            (new Regex(@"^<b>Forge folder:</b>  (.+)$"), "<b>Pasta do Forge:</b>  {1}", "<b>Carpeta de Forge:</b>  {1}"),
            (new Regex(@"^<b>Forge program:</b>  (.+)$"), "<b>Programa do Forge:</b>  {1}", "<b>Programa de Forge:</b>  {1}"),
            (new Regex(@"^<b>Bridge:</b>  (.+)$"), "<b>Ponte:</b>  {1}", "<b>Puente:</b>  {1}"),
            (new Regex(@"^<b>MTG United folder:</b>  (.+)$"), "<b>Pasta do MTG United:</b>  {1}", "<b>Carpeta de MTG United:</b>  {1}"),
            (new Regex(@"^Java:  not found \(install Java 17\+\)$"), "Java:  não encontrado (instale o Java 17+)", "Java:  no encontrado (instala Java 17+)"),
            (new Regex(@"^Use (.*[\\/].*)$"), "Usar {1}", "Usar {1}"),
            (new Regex(@"^STEP (\d+) OF (\d+)$"), "PASSO {1} DE {2}", "PASO {1} DE {2}"),
            (new Regex(@"^— Turn (\d+): (.+) —$"), "— Turno {1}: {2} —", "— Turno {1}: {2} —"),
            (new Regex(@"^(.+) drew a card\.$"), "{1} comprou uma carta.", "{1} robó una carta."),
            (new Regex(@"^(.+) drew (\d+) cards\.$"), "{1} comprou {2} cartas.", "{1} robó {2} cartas."),
            (new Regex(@"^(.+) tried to draw from an empty library\.$"), "{1} tentou comprar de um grimório vazio.", "{1} intentó robar de una biblioteca vacía."),
            (new Regex(@"^(.+) shuffled their library\.$"), "{1} embaralhou o grimório.", "{1} barajó su biblioteca."),
            (new Regex(@"^(.+) milled (\d+)\.$"), "{1} moeu {2}.", "{1} molió {2}."),
            (new Regex(@"^(.+) token left the battlefield\.$"), "A ficha {1} saiu do campo de batalha.", "La ficha {1} dejó el campo de batalla."),
            (new Regex(@"^(.+) untapped everything\.$"), "{1} desvirou tudo.", "{1} enderezó todo."),
            (new Regex(@"^(.+) created a (.+) token\.$"), "{1} criou uma ficha {2}.", "{1} creó una ficha {2}."),
            (new Regex(@"^(.+) mulliganed\.$"), "{1} fez mulligan.", "{1} hizo mulligan."),
            (new Regex(@"^(.+) exiled their graveyard\.$"), "{1} exilou o cemitério.", "{1} exilió su cementerio."),
            (new Regex(@"^(.+) rolled a d(\d+): (\d+)$"), "{1} rolou um d{2}: {3}", "{1} tiró un d{2}: {3}"),
            (new Regex(@"^(.+) flipped a coin: (heads|tails)$"), "{1} jogou uma moeda: {2}", "{1} lanzó una moneda: {2}"),
            (new Regex(@"^(.+) flipped (heads|tails)$"), "{1} tirou {2}", "{1} sacó {2}"),
            (new Regex(@"^Random player: (.+)$"), "Jogador aleatório: {1}", "Jugador aleatorio: {1}"),
            (new Regex(@"^(.+) revealed (.+) from the top of their library\.$"), "{1} revelou {2} do topo do grimório.", "{1} reveló {2} de la parte superior de su biblioteca."),
            (new Regex(@"^(.+) revealed (.+)\.$"), "{1} revelou {2}.", "{1} reveló {2}."),
            (new Regex(@"^(.+) reveals$"), "{1} revela", "{1} revela"),
            (new Regex(@"^(.+) undid the last action\.$"), "{1} desfez a última ação.", "{1} deshizo la última acción."),
            (new Regex(@"^(.+) scries (\d+)\.$"), "{1} vidência {2}.", "{1} adivina {2}."),
            (new Regex(@"^(.+) surveils (\d+)\.$"), "{1} investiga {2}.", "{1} vigila {2}."),
            (new Regex(@"^(.+) looked at the top (\d+) cards of their library\.$"), "{1} olhou as {2} cartas do topo do grimório.", "{1} miró las {2} cartas superiores de su biblioteca."),
            (new Regex(@"^(.+) searched their library\.$"), "{1} procurou no grimório.", "{1} buscó en su biblioteca."),
            (new Regex(@"^(.+): (.+) transformed into (.+)\.$"), "{1}: {2} transformou-se em {3}.", "{1}: {2} se transformó en {3}."),
            (new Regex(@"^(.+): (.+) transformed back into (.+)\.$"), "{1}: {2} voltou a ser {3}.", "{1}: {2} volvió a ser {3}."),
            (new Regex(@"^(.+) put a note on (.+)\.$"), "{1} pôs uma anotação em {2}.", "{1} puso una nota en {2}."),
            (new Regex(@"^(.+) removed the note from (.+)\.$"), "{1} tirou a anotação de {2}.", "{1} quitó la nota de {2}."),
            (new Regex(@"^(.+) removed (.+) from the game\.$"), "{1} removeu {2} do jogo.", "{1} quitó {2} del juego."),
            (new Regex(@"^(.+) gave control of (.+) to (.+)\.$"), "{1} deu o controle de {2} para {3}.", "{1} dio el control de {2} a {3}."),
            (new Regex(@"^(.+): life set to (-?\d+)$"), "{1}: vida definida para {2}", "{1}: vida fijada en {2}"),
            (new Regex(@"^(.+): ([+-]?\d+) life \((-?\d+)\)$"), "{1}: {2} de vida ({3})", "{1}: {2} de vida ({3})"),
            (new Regex(@"^(.+): poison (\d+)$"), "{1}: veneno {2}", "{1}: veneno {2}"),
            (new Regex(@"^(.+): (.+) played$"), "{1}: {2} jogada", "{1}: {2} jugada"),
            (new Regex(@"^(.+): (.+) → (.+)$"), "{1}: {2} → {3}", "{1}: {2} → {3}"),
            (new Regex(@"^Sit here \(play as (.+)\)$"), "Sentar aqui (jogar como {1})", "Sentarse aquí (jugar como {1})"),
            (new Regex(@"^(.+) HOW MANY\?$"), "{1} QUANTAS?", "{1} ¿CUÁNTAS?"),
            (new Regex(@"^Top (\d+) of your library \(top first\)$"), "{1} do topo do seu grimório (topo primeiro)", "{1} superiores de tu biblioteca (la de arriba primero)"),
            (new Regex(@"^Scry (\d+) · top first · right-click: To deck bottom \(Y\), or leave it on top$"), "Vidência {1} · topo primeiro · botão direito: Para o fundo (Y), ou deixe no topo", "Adivinar {1} · arriba primero · clic derecho: Al fondo (Y), o déjala arriba"),
            (new Regex(@"^Surveil (\d+) · top first · right-click: Discard \(D\) to the graveyard, or leave it on top$"), "Investigar {1} · topo primeiro · botão direito: Descartar (D) no cemitério, ou deixe no topo", "Vigilar {1} · arriba primero · clic derecho: Descartar (D) al cementerio, o déjala arriba"),
            (new Regex(@"^NOTE · (.+)$"), "ANOTAÇÃO · {1}", "NOTA · {1}"),
            (new Regex(@"^Bot (\d+)$"), "Bot {1}", "Bot {1}"),
        };

        static void EnsureTable()
        {
            if (table != null) return;
            table = new Dictionary<string, string[]>();
            foreach (var raw in Data.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                string en = parts[0].Replace("\\n", "\n");
                table[en.ToLowerInvariant()] = new[] { parts[1].Replace("\\n", "\n"), parts[2].Replace("\\n", "\n") };
            }
        }

        // English | Português (Brasil) | Español   (one per line; "\n" for line breaks)
        const string Data = @"
# ---------------------------------------------------------------- general
OK|OK|OK
Cancel|Cancelar|Cancelar
Done|Concluído|Listo
Close|Fechar|Cerrar
Back|Voltar|Atrás
Next|Próximo|Siguiente
Apply|Aplicar|Aplicar
Copy|Copiar|Copiar
Save|Salvar|Guardar
Discard|Descartar|Descartar
Keep editing|Continuar editando|Seguir editando
Delete|Excluir|Eliminar
Remove|Remover|Quitar
Rename|Renomear|Renombrar
Edit|Editar|Editar
Duplicate|Duplicar|Duplicar
Export|Exportar|Exportar
Export...|Exportar...|Exportar...
Import|Importar|Importar
Refresh|Atualizar|Actualizar
Open|Abrir|Abrir
Join|Entrar|Unirse
Start|Começar|Empezar
Start game|Começar partida|Empezar partida
Search|Buscar|Buscar
Searching...|Buscando...|Buscando...
Confirm|Confirmar|Confirmar
Skip|Pular|Omitir
Clear|Limpar|Borrar
Create|Criar|Crear
Everything|Tudo|Todo
All|Tudo|Todo
None|Nenhum|Ninguno
ON|LIGADO|SÍ
OFF|DESLIGADO|NO
Normal|Normal|Normal
Fast|Rápido|Rápido
Strong|Forte|Fuerte
Instant|Instantâneo|Instantáneo
instant|instantâneo|instantáneo
Random|Aleatório|Aleatorio
Menu|Menu|Menú
Main menu|Menu principal|Menú principal
Play|Jogar|Jugar
Play again|Jogar de novo|Jugar otra vez
Again|De novo|Otra vez
Resume|Continuar|Continuar
Options|Opções|Opciones
Settings|Configurações|Ajustes
Shortcuts|Atalhos|Atajos
Back to main menu|Voltar ao menu principal|Volver al menú principal
Mute / unmute sound|Silenciar / ativar som|Silenciar / activar sonido
These shortcuts|Estes atalhos|Estos atajos
Close the top pop-up|Fechar o pop-up de cima|Cerrar la ventana de arriba
Victory|Vitória|Victoria
Defeat|Derrota|Derrota
The game is over.|A partida acabou.|La partida terminó.
You won the pod!|Você venceu a mesa!|¡Ganaste la mesa!
Sound on|Som ligado|Sonido activado
Sound muted|Som desligado|Sonido silenciado
Game started. Good luck!|Partida iniciada. Boa sorte!|Partida iniciada. ¡Buena suerte!
Enter a number|Digite um número|Escribe un número
Type here|Digite aqui|Escribe aquí
Type a card name...|Digite o nome de uma carta...|Escribe el nombre de una carta...
Click a name to choose it.|Clique em um nome para escolher.|Haz clic en un nombre para elegirlo.
Choose a card|Escolha uma carta|Elige una carta
View board|Ver mesa|Ver mesa
Show cards|Mostrar cartas|Mostrar cartas
Keep this order|Manter esta ordem|Mantener este orden
Show dialog|Mostrar janela|Mostrar ventana
keep open|manter aberto|mantener abierto
keep the menu open|manter o menu aberto|mantener el menú abierto
Peek|Espiar|Mirar
Life|Vida|Vida
Thinking|Pensando|Pensando
Poison|Veneno|Veneno
Pay|Pagar|Pagar
Don't pay|Não pagar|No pagar
right-click: all revealed cards|botão direito: todas as cartas reveladas|clic derecho: todas las cartas reveladas
Error log|Log de erros|Registro de errores
Update this Forge?|Atualizar este Forge?|¿Actualizar este Forge?
Update this Forge to the newest release|Atualizar este Forge para a versão mais nova|Actualizar este Forge a la versión más nueva
MTG United downloads the newest official Forge release from GitHub and installs it over this folder, then builds the bridge for it. Your Forge settings and decks are kept.|O MTG United baixa a versão oficial mais nova do Forge no GitHub e instala nesta pasta, depois compila a ponte para ela. Suas configurações e decks do Forge são mantidos.|MTG United descarga la versión oficial más nueva de Forge desde GitHub y la instala en esta carpeta; luego compila el puente para ella. Tus ajustes y mazos de Forge se conservan.
Click the card or player it points at (Esc to cancel).|Clique na carta ou jogador para onde ela aponta (Esc cancela).|Haz clic en la carta o jugador al que apunta (Esc cancela).
Point at a card or player…|Apontar para uma carta ou jogador…|Apuntar a una carta o jugador…
Clear my arrows|Limpar minhas setas|Borrar mis flechas
Point at a card or player (then click it); pointing again removes the arrow|Apontar para uma carta ou jogador (depois clique nele); apontar de novo remove a seta|Apuntar a una carta o jugador (luego haz clic); apuntar otra vez quita la flecha
Clear your arrows (they also clear each turn)|Limpar suas setas (elas também somem a cada turno)|Borrar tus flechas (también se borran cada turno)
Download Java|Baixar Java|Descargar Java
Copy output|Copiar saída|Copiar salida
Opening Java's download page. Install it, then restart MTG United.|Abrindo a página de download do Java. Instale e depois reinicie o MTG United.|Abriendo la página de descarga de Java. Instálalo y reinicia MTG United.
Nothing to copy yet.|Nada para copiar ainda.|Nada que copiar todavía.
Library — revealed cards|Grimório — cartas reveladas|Biblioteca — cartas reveladas
no revealed cards in the library|nenhuma carta revelada no grimório|ninguna carta revelada en la biblioteca
commander is not in the command zone|o comandante não está na zona de comando|el comandante no está en la zona de mando
Big view of the card under the mouse (any screen)|Visão ampliada da carta sob o mouse (em qualquer tela)|Vista ampliada de la carta bajo el ratón (en cualquier pantalla)
Show set codes in names|Mostrar códigos de coleção|Mostrar códigos de colección
Play with friends|Jogue com amigos|Juega con amigos
Host a table or join one on your network. Only the host needs Forge.|Crie uma mesa ou entre em uma na sua rede. Só o anfitrião precisa do Forge.|Crea una mesa o únete a una en tu red. Solo el anfitrión necesita Forge.
Change|Alterar|Cambiar
A free table: nothing is enforced, custom sets welcome.|Mesa livre: nada é imposto, coleções próprias são bem-vindas.|Mesa libre: nada se impone, colecciones propias bienvenidas.
AI|IA|IA
Random decks|Decks aleatórios|Mazos aleatorios
Precon decks|Decks pré-montados|Mazos precon
Choose a deck|Escolha um deck|Elige un mazo
Any deck, any size. Nothing is enforced; custom sets welcome.|Qualquer deck, qualquer tamanho. Nada é imposto; coleções próprias são bem-vindas.|Cualquier mazo, cualquier tamaño. Nada se impone; colecciones propias bienvenidas.
Players|Jogadores|Jugadores
AI opponents|Oponentes IA|Rivales IA
Other seats|Outros lugares|Otros asientos
Their decks|Decks deles|Sus mazos
Playing alone? Pick each seat's deck to goldfish a matchup.|Jogando sozinho? Escolha o deck de cada lugar para testar um confronto.|¿Juegas solo? Elige el mazo de cada asiento para probar un enfrentamiento.
Forge isn't running yet: start it in the Forge tab.|O Forge ainda não está rodando: inicie-o na aba Forge.|Forge aún no está en marcha: inícialo en la pestaña Forge.
Kitchen table: any deck works, nothing is enforced.|Mesa da cozinha: qualquer deck serve, nada é imposto.|Mesa de cocina: cualquier mazo sirve, nada se impone.
Commander: 100 cards, led by a legendary commander.|Commander: 100 cartas, lideradas por um comandante lendário.|Commander: 100 cartas, lideradas por un comandante legendario.
40 cards: limited-style decks, 40 cards or more.|40 cartas: decks estilo limitado, 40 cartas ou mais.|40 cartas: mazos estilo limitado, 40 cartas o más.
60 cards: constructed decks, 60 cards or more.|60 cartas: decks construídos, 60 cartas ou mais.|60 cartas: mazos construidos, 60 cartas o más.
My decks|Meus decks|Mis mazos
Other formats (they still play, as the chosen format)|Outros formatos (também jogam, no formato escolhido)|Otros formatos (también se juegan, en el formato elegido)
A ready-made deck|Um deck pronto|Un mazo ya armado
cards|cartas|cartas
Open source|Código aberto|Código abierto
MTG United on GitHub|MTG United no GitHub|MTG United en GitHub
Follow the project, grab new versions, report bugs and suggest features. Stars help a lot!|Acompanhe o projeto, baixe novas versões, reporte bugs e sugira ideias. Estrelas ajudam muito!|Sigue el proyecto, descarga nuevas versiones, informa errores y sugiere ideas. ¡Las estrellas ayudan mucho!
Open GitHub|Abrir GitHub|Abrir GitHub
Concede|Conceder|Rendirse
Exit game|Sair do jogo|Salir del juego
Exit|Sair|Salir
Gameplay|Jogabilidade|Jugabilidad
Graphics|Gráficos|Gráficos
Audio|Áudio|Audio
Report a bug|Reportar um bug|Informar de un error
Exit MTG United?|Sair do MTG United?|¿Salir de MTG United?
The game in progress will be lost.|A partida em andamento será perdida.|Se perderá la partida en curso.
See you next time.|Até a próxima.|Hasta la próxima.
Language|Idioma|Idioma
# ---------------------------------------------------------------- zones & game words
Hand|Mão|Mano
Library|Grimório|Biblioteca
Graveyard|Cemitério|Cementerio
Exile|Exílio|Exilio
Command|Comando|Mando
Command zone|Zona de comando|Zona de mando
Battlefield|Campo de batalha|Campo de batalla
Stack|Pilha|Pila
Mana|Mana|Maná
Turn|Turno|Turno
Your turn|Seu turno|Tu turno
Pass|Passar|Pasar
Resolve|Resolver|Resolver
End Turn|Encerrar turno|Terminar turno
Undo|Desfazer|Deshacer
Undo.|Desfeito.|Deshecho.
Keep|Manter|Quedarse
Keep hand|Manter mão|Quedarse la mano
Mulligan|Mulligan|Mulligan
Attack|Atacar|Atacar
Block|Bloquear|Bloquear
Damage|Dano|Daño
Combat|Combate|Combate
Upkeep|Manutenção|Mantenimiento
Untap|Desvirar|Enderezar
Tap|Virar|Girar
Draw|Comprar|Robar
Main 1|Principal 1|Principal 1
Main 2|Principal 2|Principal 2
End cbt|Fim cmb|Fin cmb
End|Final|Final
Cleanup|Limpeza|Limpieza
Phase stops: click = your turn (orange), right-click = opponents' turns (blue)|Paradas de fase: clique = seu turno (laranja), botão direito = turnos dos oponentes (azul)|Paradas de fase: clic = tu turno (naranja), clic derecho = turnos rivales (azul)
Revealed|Revelada|Revelada
Cards revealed this game|Cartas reveladas nesta partida|Cartas reveladas en esta partida
Creature|Criatura|Criatura
Land|Terreno|Tierra
Lands|Terrenos|Tierras
Nonlands|Não terrenos|No tierras
Nonland|Não terreno|No tierra
Legendary|Lendária|Legendaria
Basic|Básica|Básica
Token|Ficha|Ficha
Commander|Commander|Commander
60 cards|60 cartas|60 cartas
40 cards|40 cartas|40 cartas
60-card|60 cartas|60 cartas
40-card|40 cartas|40 cartas
Format|Formato|Formato
# ---------------------------------------------------------------- main menu
Home|Início|Inicio
Profile|Perfil|Perfil
Decks|Decks|Mazos
Deck|Deck|Mazo
Forge|Forge|Forge
Table|Mesa|Mesa
Kitchen Table|Mesa da Cozinha|Mesa de Cocina
KITCHEN TABLE|MESA DA COZINHA|MESA DE COCINA
NEW · KITCHEN TABLE|NOVO · MESA DA COZINHA|NUEVO · MESA DE COCINA
Gather your pod|Reúna sua mesa|Reúne tu mesa
Commander with full rules from the Forge engine, against up to five AI opponents. Pick a deck, set the pod, press Play.|Commander com todas as regras do motor Forge, contra até cinco oponentes IA. Escolha um deck, monte a mesa e aperte Jogar.|Commander con todas las reglas del motor Forge, contra hasta cinco rivales IA. Elige un mazo, arma la mesa y pulsa Jugar.
Play your own cards|Jogue com suas próprias cartas|Juega con tus propias cartas
How it works|Como funciona|Cómo funciona
HOW IT WORKS|COMO FUNCIONA|CÓMO FUNCIONA
Import a set|Importar uma coleção|Importar una colección
Rules engine|Motor de regras|Motor de reglas
Forge Bridge|Ponte do Forge|Puente de Forge
Manage Forge|Gerenciar Forge|Gestionar Forge
Start Forge|Iniciar Forge|Iniciar Forge
A free table for playtesting and casual games, like Cockatrice: you move the cards, nothing is enforced.|Uma mesa livre para testes e partidas casuais, como o Cockatrice: você move as cartas, nada é imposto.|Una mesa libre para pruebas y partidas casuales, como Cockatrice: tú mueves las cartas, nada se impone.
A free table like Cockatrice: nothing is enforced, custom sets welcome, no Forge needed. PLAY seats everyone on this screen.|Uma mesa livre como o Cockatrice: nada é imposto, coleções próprias são bem-vindas e não precisa do Forge. JOGAR senta todos nesta tela.|Una mesa libre como Cockatrice: nada se impone, colecciones propias bienvenidas, sin Forge. JUGAR sienta a todos en esta pantalla.
or with friends over your local network. No rules engine and no Forge needed.|ou com amigos pela sua rede local. Sem motor de regras e sem precisar do Forge.|o con amigos en tu red local. Sin motor de reglas y sin Forge.
Custom sets from Magic Set Editor|Coleções próprias do Magic Set Editor|Colecciones propias de Magic Set Editor
Changed the set in MSE? Export again and import over it: same set code, it's replaced.|Mudou a coleção no MSE? Exporte de novo e importe por cima: mesmo código, ela é substituída.|¿Cambiaste la colección en MSE? Exporta otra vez e impórtala encima: mismo código, se reemplaza.
My sets|Minhas coleções|Mis colecciones
Play at the table|Jogar na mesa|Jugar en la mesa
My Decks|Meus Decks|Mis Mazos
Edit Deck|Editar Deck|Editar Mazo
New deck|Novo deck|Nuevo mazo
New Deck|Novo Deck|Nuevo Mazo
Untitled Deck|Deck sem nome|Mazo sin nombre
Imported Deck|Deck importado|Mazo importado
Select a deck to rename.|Selecione um deck para renomear.|Selecciona un mazo para renombrar.
Select a deck to export.|Selecione um deck para exportar.|Selecciona un mazo para exportar.
Select a deck first, or create one with +.|Selecione um deck primeiro, ou crie um com +.|Selecciona un mazo primero, o crea uno con +.
Select a deck to delete.|Selecione um deck para excluir.|Selecciona un mazo para eliminar.
Select a deck to duplicate.|Selecione um deck para duplicar.|Selecciona un mazo para duplicar.
Remove from favourites|Remover dos favoritos|Quitar de favoritos
Add to favourites|Adicionar aos favoritos|Añadir a favoritos
Delete this deck?|Excluir este deck?|¿Eliminar este mazo?
Deck name|Nome do deck|Nombre del mazo
Player name|Nome do jogador|Nombre del jugador
Options (size, sound, auto-pay)|Opções (tamanho, som, pagamento automático)|Opciones (tamaño, sonido, pago automático)
Clear card image cache|Limpar cache de imagens|Borrar caché de imágenes
Reset stats|Zerar estatísticas|Reiniciar estadísticas
AI decks|Decks da IA|Mazos de la IA
Precons|Pré-montados|Precons
Random precon|Pré-montado aleatório|Precon aleatorio
Choose their decks|Escolher os decks deles|Elegir sus mazos
AI Skill|Nível da IA|Nivel de la IA
Mode|Modo|Modo
Mode & AI skill|Modo e nível da IA|Modo y nivel de la IA
Forge rules|Regras do Forge|Reglas de Forge
Play on LAN (host / join)|Jogar na rede (criar / entrar)|Jugar en red (crear / unirse)
Play on LAN|Jogar na rede|Jugar en red
Click to choose one of your decks|Clique para escolher um dos seus decks|Haz clic para elegir uno de tus mazos
No commander set|Sem comandante definido|Sin comandante
Connected to Forge|Conectado ao Forge|Conectado a Forge
MTG United's copy of Forge|Cópia do Forge do MTG United|Copia de Forge de MTG United
Looking for the bridge...|Procurando a ponte...|Buscando el puente...
Pick your deck first (Decks tab).|Escolha seu deck primeiro (aba Decks).|Elige tu mazo primero (pestaña Mazos).
Pick a deck first (Decks tab).|Escolha um deck primeiro (aba Decks).|Elige un mazo primero (pestaña Mazos).
Other seats' decks|Decks dos outros lugares|Mazos de los otros asientos
Automatic (your other decks in turn)|Automático (seus outros decks, em sequência)|Automático (tus otros mazos, por turnos)
Same deck as yours (mirror)|O mesmo deck que o seu (espelho)|El mismo mazo que el tuyo (espejo)
same as yours|igual ao seu|igual al tuyo
You're seated at someone's LAN table: the host starts the game.|Você está sentado na mesa de rede de alguém: o anfitrião começa a partida.|Estás sentado en la mesa en red de alguien: el anfitrión empieza la partida.
MTG United uses Forge as its rules engine. Install it, or point MTG United at a Forge you already have; nothing is downloaded twice.|O MTG United usa o Forge como motor de regras. Instale-o, ou aponte para um Forge que você já tem; nada é baixado duas vezes.|MTG United usa Forge como motor de reglas. Instálalo, o indica un Forge que ya tengas; nada se descarga dos veces.
# ---------------------------------------------------------------- options
Resolution|Resolução|Resolución
Display mode|Modo de tela|Modo de pantalla
Interface size|Tamanho da interface|Tamaño de la interfaz
Card size|Tamanho das cartas|Tamaño de las cartas
Text size|Tamanho do texto|Tamaño del texto
Card preview delay|Atraso da prévia da carta|Retraso de la vista previa
Volume|Volume|Volumen
Animation speed|Velocidade das animações|Velocidad de animación
Reduce motion|Reduzir movimento|Reducir movimiento
Sound|Som|Sonido
Auto-pay mana|Pagar mana automaticamente|Pagar maná automáticamente
Windowed|Janela|Ventana
Fullscreen|Tela cheia|Pantalla completa
Borderless|Sem bordas|Sin bordes
Maximized|Maximizada|Maximizada
# ---------------------------------------------------------------- shortcuts
Forge game|Partida Forge|Partida Forge
Deck builder|Editor de decks|Editor de mazos
Everywhere|Em todo lugar|En todas partes
Pass, OK, Resolve (main button)|Passar, OK, Resolver (botão principal)|Pasar, OK, Resolver (botón principal)
Cancel what Forge asks / open the menu|Cancelar o que o Forge pede / abrir o menu|Cancelar lo que pide Forge / abrir el menú
End turn (pass until your next turn)|Encerrar turno (passar até seu próximo turno)|Terminar turno (pasar hasta tu siguiente turno)
Attack with everything|Atacar com tudo|Atacar con todo
Focus that opponent's board|Focar o campo desse oponente|Enfocar el campo de ese rival
Focus the next opponent|Focar o próximo oponente|Enfocar al siguiente rival
Focus your own board (or scroll up over it)|Focar o seu campo (ou role para cima sobre ele)|Enfocar tu campo (o desplázate hacia arriba sobre él)
Show every board again|Mostrar todos os campos de novo|Mostrar todos los campos otra vez
Your graveyard / exile / commander|Seu cemitério / exílio / comandante|Tu cementerio / exilio / comandante
Table:|Mesa:|Mesa:
Cards:|Cartas:|Cartas:
Piles:|Pilhas:|Pilas:
Selecting:|Seleção:|Selección:
Other:|Outros:|Otros:
Draw a card|Comprar uma carta|Robar una carta
Untap all your permanents|Desvirar todas as suas permanentes|Enderezar todos tus permanentes
Shuffle your library|Embaralhar seu grimório|Barajar tu biblioteca
Mulligan (new seven)|Mulligan (sete novas)|Mulligan (siete nuevas)
Create a token|Criar uma ficha|Crear una ficha
Dice, coin and random player|Dados, moeda e jogador aleatório|Dados, moneda y jugador aleatorio
Undo the last action|Desfazer a última ação|Deshacer la última acción
Clear the selection / open the menu|Limpar a seleção / abrir o menu|Borrar la selección / abrir el menú
Tap / untap|Virar / desvirar|Girar / enderezar
Summoning sick (clears on its controller's turn)|Enjoo de invocação (some no turno do controlador)|Mareo de invocación (se quita en el turno de su controlador)
Flip: transform a double-faced card, or face down / up|Virar: transformar uma carta de duas faces, ou face para baixo / cima|Voltear: transformar una carta de dos caras, o boca abajo / arriba
Turn left / right|Girar para a esquerda / direita|Girar a la izquierda / derecha
+1/+1 / −1/−1 counter (they cancel out)|Marcador +1/+1 / −1/−1 (eles se anulam)|Contador +1/+1 / −1/−1 (se anulan)
Add / remove a counter|Adicionar / remover um marcador|Añadir / quitar un contador
Copy it (token)|Copiar (ficha)|Copiar (ficha)
Delete it from the game|Remover do jogo|Eliminar de la partida
Play it|Jogar|Jugarla
To hand / discard (graveyard) / exile|Para a mão / descartar (cemitério) / exilar|A la mano / descartar (cementerio) / exiliar
To the top / bottom of its deck|Para o topo / fundo do grimório|Arriba / abajo de su biblioteca
To the command zone|Para a zona de comando|A la zona de mando
Add or edit a note|Adicionar ou editar uma nota|Añadir o editar una nota
Reveal it (from your hand)|Revelar (da sua mão)|Revelarla (de tu mano)
Hold Alt|Segure Alt|Mantén Alt
Zoom in on it|Ampliar|Ampliarla
Move the pile's top card (like a card)|Mover a carta do topo da pilha (como uma carta)|Mover la carta superior de la pila (como una carta)
Over the library: shuffle / mulligan|Sobre o grimório: embaralhar / mulligan|Sobre la biblioteca: barajar / mulligan
Shift + click|Shift + clique|Shift + clic
Add a card to the selection|Adicionar uma carta à seleção|Añadir una carta a la selección
Drag the table|Arrastar na mesa|Arrastrar en la mesa
Select every card in the box|Selecionar todas as cartas na caixa|Seleccionar todas las cartas del recuadro
Drag a selected card|Arrastar uma carta selecionada|Arrastrar una carta seleccionada
Moves the whole selection|Move a seleção inteira|Mueve toda la selección
Click / Dbl-click|Clique / duplo clique|Clic / doble clic
Tap / play a card from your hand|Virar / jogar uma carta da mão|Girar / jugar una carta de tu mano
Right-click|Botão direito|Clic derecho
Every action for that card or pile|Todas as ações dessa carta ou pilha|Todas las acciones de esa carta o pila
Drag a pile|Arrastar uma pilha|Arrastrar una pila
Take its top card|Pegar a carta do topo|Tomar la carta superior
Ctrl + click|Ctrl + clique|Ctrl + clic
A seat's nameplate: look from their side (alone)|Placa de um lugar: ver pelo lado dele (sozinho)|Placa de un asiento: mirar desde su lado (solo)
Click|Clique|Clic
Add a card (all cards) / remove one copy (your deck)|Adicionar uma carta (todas) / remover uma cópia (seu deck)|Añadir una carta (todas) / quitar una copia (tu mazo)
Drag|Arrastar|Arrastrar
To the list: add  ·  out of the deck: remove one|Para a lista: adicionar  ·  para fora do deck: remover uma|A la lista: añadir  ·  fuera del mazo: quitar una
Drag to art|Arrastar para a arte|Arrastrar al arte
Make that card the deck's cover|Usar essa carta como capa do deck|Usar esa carta como portada del mazo
Zoom in on the card|Ampliar a carta|Ampliar la carta
Save without closing|Salvar sem fechar|Guardar sin cerrar
Close the editor (asks to save changes)|Fechar o editor (pergunta se quer salvar)|Cerrar el editor (pregunta si guardar)
# ---------------------------------------------------------------- kitchen table
Table log|Registro da mesa|Registro de la mesa
Next turn|Próximo turno|Siguiente turno
Untap all|Desvirar tudo|Enderezar todo
Shuffle|Embaralhar|Barajar
Dice & coin|Dados e moeda|Dados y moneda
View|Ver|Ver
Draw X…|Comprar X…|Robar X…
Scry X…|Vidência X…|Adivinar X…
Scry|Vidência|Adivinar
Surveil X…|Vigiar X…|Vigilar X…
Surveil|Vigiar|Vigilar
Mill|Moer|Moler
Play the top card|Jogar a carta do topo|Jugar la carta superior
Reveal the top card|Revelar a carta do topo|Revelar la carta superior
To deck bottom|Para o fundo do grimório|Al fondo de la biblioteca
To deck top|Para o topo do grimório|Arriba de la biblioteca
Tap them|Virar todas|Girarlas
Untap them|Desvirar todas|Enderezarlas
Not summoning sick|Sem enjoo de invocação|Sin mareo de invocación
Summoning sick|Enjoo de invocação|Mareo de invocación
zZz  Summoning sick|zZz  Enjoo de invocação|zZz  Mareo de invocación
Transform to the front|Transformar para a frente|Transformar al frente
Transform to the back|Transformar para o verso|Transformar al reverso
Flip (face down / up)|Virar (face para baixo / cima)|Voltear (boca abajo / arriba)
Edit the note|Editar a nota|Editar la nota
Add a note|Adicionar uma nota|Añadir una nota
Zoom|Ampliar|Ampliar
That card has no other face.|Essa carta não tem outra face.|Esa carta no tiene otra cara.
face-down card|carta virada para baixo|carta boca abajo
a face-down card|uma carta virada para baixo|una carta boca abajo
Click a card for actions  ·  right-click to zoom|Clique numa carta para ações  ·  botão direito para ampliar|Haz clic en una carta para acciones  ·  clic derecho para ampliar
Create a token|Criar uma ficha|Crear una ficha
Soldier, Treasure, Spirit...|Soldado, Tesouro, Espírito...|Soldado, Tesoro, Espíritu...
Power/Toughness|Poder/Resistência|Fuerza/Resistencia
From your custom sets|Das suas coleções próprias|De tus colecciones propias
No custom sets imported yet. Type a name above.|Nenhuma coleção própria importada ainda. Digite um nome acima.|Aún no hay colecciones propias. Escribe un nombre arriba.
No custom tokens match.|Nenhuma ficha própria corresponde.|Ninguna ficha propia coincide.
Everyone at the table sees it under the card. It's removed when the card leaves the battlefield.|Todos na mesa veem abaixo da carta. Ela some quando a carta sai do campo de batalha.|Todos en la mesa la ven bajo la carta. Se quita cuando la carta sale del campo de batalla.
Note|Nota|Nota
Write a note...|Escreva uma nota...|Escribe una nota...
Save note|Salvar nota|Guardar nota
Remove note|Remover nota|Quitar nota
In the lobby. Waiting for the host to start...|No saguão. Esperando o anfitrião começar...|En la sala. Esperando a que el anfitrión empiece...
Disconnected from the host.|Desconectado do anfitrião.|Desconectado del anfitrión.
Disconnected from the host's table.|Desconectado da mesa do anfitrião.|Desconectado de la mesa del anfitrión.
LAN table|Mesa em rede|Mesa en red
Joined. The host starts the game when everyone is in.|Entrou. O anfitrião começa a partida quando todos estiverem dentro.|Te uniste. El anfitrión empieza cuando estén todos.
Open a table, then have the others join from their PCs (same Wi-Fi / network).|Abra uma mesa e peça para os outros entrarem dos PCs deles (mesmo Wi-Fi / rede).|Abre una mesa y que los demás se unan desde sus PCs (misma Wi-Fi / red).
Nobody here yet|Ninguém aqui ainda|Aún no hay nadie
No luck? Make sure you're on the same network and the host allowed MTG United through the Windows firewall, or type the address below.|Nada? Confira se estão na mesma rede e se o anfitrião liberou o MTG United no firewall do Windows, ou digite o endereço abaixo.|¿Nada? Comprueba que estáis en la misma red y que el anfitrión permitió MTG United en el firewall de Windows, o escribe la dirección abajo.
# ---------------------------------------------------------------- deck builder & dialogs
Save changes?|Salvar alterações?|¿Guardar cambios?
Search cards...|Buscar cartas...|Buscar cartas...
all cards|todas as cartas|todas las cartas
Deck as text|Deck como texto|Mazo como texto
Deck list copied|Lista do deck copiada|Lista del mazo copiada
Deck saved|Deck salvo|Mazo guardado
find cards|buscar cartas|buscar cartas
Card pool|Coleção de cartas|Colección de cartas
Showing: your deck|Mostrando: seu deck|Mostrando: tu mazo
Showing: all cards|Mostrando: todas as cartas|Mostrando: todas las cartas
Collection view|Visão de coleção|Vista de colección
Column view|Visão em colunas|Vista en columnas
Real cards|Cartas reais|Cartas reales
Import custom sets in Decks → Custom Sets|Importe coleções próprias em Decks → Coleções Próprias|Importa colecciones propias en Mazos → Colecciones Propias
Click a card for − / +  ·  drag it out to remove one  ·  right-click to zoom|Clique numa carta para − / +  ·  arraste para fora para remover uma  ·  botão direito para ampliar|Clic en una carta para − / +  ·  arrástrala fuera para quitar una  ·  clic derecho para ampliar
Type a name, or Scryfall syntax: t:creature c:rg mv<=3 o:draw|Digite um nome, ou a sintaxe do Scryfall: t:creature c:rg mv<=3 o:draw|Escribe un nombre, o la sintaxis de Scryfall: t:creature c:rg mv<=3 o:draw
Click (or drag out) to remove one copy  ·  drag a card onto the art by the name to make it the cover|Clique (ou arraste para fora) para remover uma cópia  ·  arraste uma carta para a arte ao lado do nome para usá-la como capa|Haz clic (o arrastra fuera) para quitar una copia  ·  arrastra una carta al arte junto al nombre para usarla de portada
Paste a list (Arena, Moxfield, MTGO) to replace this deck, or copy it out. Put commanders under a ""Commander"" line.|Cole uma lista (Arena, Moxfield, MTGO) para substituir este deck, ou copie-a. Coloque os comandantes abaixo de uma linha ""Commander"".|Pega una lista (Arena, Moxfield, MTGO) para reemplazar este mazo, o cópiala. Pon los comandantes bajo una línea ""Commander"".
Custom sets|Coleções próprias|Colecciones propias
Custom cards appear in deck builder searches and play at the kitchen table (Forge rules games use real cards only).|Cartas próprias aparecem nas buscas do editor de decks e jogam na mesa da cozinha (partidas com regras do Forge usam só cartas reais).|Las cartas propias aparecen en las búsquedas del editor y se juegan en la mesa de cocina (las partidas con reglas de Forge usan solo cartas reales).
Import set (.xml)|Importar coleção (.xml)|Importar colección (.xml)
Open sets folder|Abrir pasta das coleções|Abrir carpeta de colecciones
No custom sets yet.|Nenhuma coleção própria ainda.|Aún no hay colecciones propias.
Import a deck|Importar um deck|Importar un mazo
From a file...|De um arquivo...|Desde un archivo...
From a file (.txt / .dck / .dec)...|De um arquivo (.txt / .dck / .dec)...|Desde un archivo (.txt / .dck / .dec)...
MTGO Format|Formato MTGO|Formato MTGO
Moxfield Format|Formato Moxfield|Formato Moxfield
Archidekt Format|Formato Archidekt|Formato Archidekt
Plain Text|Texto simples|Texto plano
One card per line, with or without a number in front.|Uma carta por linha, com ou sem número na frente.|Una carta por línea, con o sin número delante.
Export → Copy for Moxfield / MTGO / Arena. Set codes and foil marks are ignored.|Exportar → Copiar para Moxfield / MTGO / Arena. Códigos de coleção e marcas de foil são ignorados.|Exportar → Copiar para Moxfield / MTGO / Arena. Se ignoran códigos de colección y marcas foil.
Copy the deck list on the site (or in MTGO), then pick where it came from. It's read from your clipboard.|Copie a lista do deck no site (ou no MTGO) e escolha de onde ela veio. Ela é lida da sua área de transferência.|Copia la lista del mazo en la web (o en MTGO) y elige de dónde viene. Se lee de tu portapapeles.
Your clipboard is empty: copy the deck list first.|Sua área de transferência está vazia: copie a lista do deck primeiro.|Tu portapapeles está vacío: copia primero la lista del mazo.
No cards found in the clipboard for that format.|Nenhuma carta encontrada na área de transferência para esse formato.|No se encontraron cartas en el portapapeles para ese formato.
# ---------------------------------------------------------------- forge LAN
LAN game · Rules engine|Partida em rede · Motor de regras|Partida en red · Motor de reglas
Host a table|Criar uma mesa|Crear una mesa
Join a table|Entrar numa mesa|Unirse a una mesa
Open table|Abrir mesa|Abrir mesa
Tables on your network|Mesas na sua rede|Mesas en tu red
…or type the host's address (e.g. 192.168.0.12)|…ou digite o endereço do anfitrião (ex.: 192.168.0.12)|…o escribe la dirección del anfitrión (p. ej. 192.168.0.12)
Start Forge first (Forge tab): the host's Forge runs the game.|Inicie o Forge primeiro (aba Forge): o Forge do anfitrião roda a partida.|Inicia Forge primero (pestaña Forge): el Forge del anfitrión ejecuta la partida.
Leave the other table first.|Saia da outra mesa primeiro.|Sal primero de la otra mesa.
Nobody to play against yet: wait for a friend or add an AI.|Ninguém para jogar ainda: espere um amigo ou adicione uma IA.|Aún no hay rivales: espera a un amigo o añade una IA.
Type the host's address first.|Digite o endereço do anfitrião primeiro.|Escribe primero la dirección del anfitrión.
Open your table, wait for friends to join, choose how many AIs fill the other seats, then Start.|Abra sua mesa, espere os amigos entrarem, escolha quantas IAs ocupam os outros lugares e clique em Começar.|Abre tu mesa, espera a que se unan tus amigos, elige cuántas IA ocupan los otros asientos y pulsa Empezar.
Table closed|Mesa fechada|Mesa cerrada
Press OPEN TABLE to let others join.|Aperte ABRIR MESA para deixar os outros entrarem.|Pulsa ABRIR MESA para que los demás se unan.
In the lobby|No saguão|En la sala
Looking for tables...|Procurando mesas...|Buscando mesas...
Same network as the host? Press REFRESH, or type the host's address below. The host must allow MTG United and Java through the Windows firewall.|Na mesma rede que o anfitrião? Aperte ATUALIZAR, ou digite o endereço dele abaixo. O anfitrião precisa liberar o MTG United e o Java no firewall do Windows.|¿En la misma red que el anfitrión? Pulsa ACTUALIZAR, o escribe su dirección abajo. El anfitrión debe permitir MTG United y Java en el firewall de Windows.
You don't need Forge to join: the host's Forge plays the rules.|Você não precisa do Forge para entrar: o Forge do anfitrião cuida das regras.|No necesitas Forge para unirte: el Forge del anfitrión aplica las reglas.
# ---------------------------------------------------------------- forge page
Tutorial|Tutorial|Tutorial
How to set up Forge|Como configurar o Forge|Cómo configurar Forge
Details|Detalhes|Detalles
Your own Forge|Seu próprio Forge|Tu propio Forge
MTG United's copy (built from Forge's source)|Cópia do MTG United (compilada do código do Forge)|Copia de MTG United (compilada del código de Forge)
Nothing yet.|Nada ainda.|Nada aún.
Install Java|Instalar Java|Instalar Java
Install Git|Instalar Git|Instalar Git
Install Git (for Install Forge)|Instalar Git (para Instalar Forge)|Instalar Git (para Instalar Forge)
Install Forge|Instalar Forge|Instalar Forge
Use my Forge...|Usar meu Forge...|Usar mi Forge...
Find Forge|Encontrar Forge|Buscar Forge
Restart bridge|Reiniciar ponte|Reiniciar puente
Stop bridge|Parar ponte|Detener puente
Start bridge|Iniciar ponte|Iniciar puente
Update|Atualizar|Actualizar
Reinstall|Reinstalar|Reinstalar
Use MTG United's Forge|Usar o Forge do MTG United|Usar el Forge de MTG United
Open folder|Abrir pasta|Abrir carpeta
Open log|Abrir log|Abrir registro
Click again to uninstall|Clique de novo para desinstalar|Haz clic otra vez para desinstalar
Uninstall|Desinstalar|Desinstalar
Keep settings & log: ON|Manter configurações e log: SIM|Mantener ajustes y registro: SÍ
Keep settings & log: OFF|Manter configurações e log: NÃO|Mantener ajustes y registro: NO
Why does MTG United need Forge?|Por que o MTG United precisa do Forge?|¿Por qué MTG United necesita Forge?
What you need|Do que você precisa|Qué necesitas
Already have Forge? Use it|Já tem o Forge? Use-o|¿Ya tienes Forge? Úsalo
No Forge yet? Install it|Ainda sem Forge? Instale|¿Sin Forge? Instálalo
Start playing|Comece a jogar|Empieza a jugar
If something goes wrong|Se algo der errado|Si algo sale mal
Output|Saída|Salida
Bridge stopped.|Ponte parada.|Puente detenido.
Not connected to the Forge bridge yet.|Ainda não conectado à ponte do Forge.|Aún no conectado al puente de Forge.
Lost connection to the Forge bridge. Reconnecting...|Conexão com a ponte do Forge perdida. Reconectando...|Se perdió la conexión con el puente de Forge. Reconectando...
Starting game... (Forge is shuffling up)|Iniciando partida... (o Forge está embaralhando)|Iniciando partida... (Forge está barajando)
Forge isn't set up yet: open the Forge tab to install it or point to your Forge.|O Forge ainda não está configurado: abra a aba Forge para instalá-lo ou apontar o seu.|Forge aún no está configurado: abre la pestaña Forge para instalarlo o indicar el tuyo.
That folder doesn't look like Forge: it needs a 'res' folder and Forge's .jar file.|Essa pasta não parece ser do Forge: ela precisa de uma pasta 'res' e do .jar do Forge.|Esa carpeta no parece de Forge: necesita una carpeta 'res' y el .jar de Forge.
# ---------------------------------------------------------------- limited (draft / sealed)
DRAFT / SEALED|DRAFT / SELADO|DRAFT / SELLADO
Draft|Draft|Draft
Sealed|Selado|Sellado
PACKS|PACOTES|SOBRES
PACK 1|PACOTE 1|SOBRE 1
PACK 2|PACOTE 2|SOBRE 2
PACK 3|PACOTE 3|SOBRE 3
bots|bots|bots
seats|lugares|asientos
8 is a classic draft table|8 é uma mesa clássica de draft|8 es una mesa clásica de draft
6 packs each|6 pacotes cada|6 sobres cada uno
Choose a set or cube|Escolha uma coleção ou cubo|Elige una colección o cubo
Same as pack 1|Igual ao pacote 1|Igual que el sobre 1
Set code (e.g. MKM, DSK, FDN)|Código da coleção (ex.: MKM, DSK, FDN)|Código de la colección (p. ej. MKM, DSK, FDN)
LOAD FROM SCRYFALL|CARREGAR DO SCRYFALL|CARGAR DE SCRYFALL
A real set (type its code)|Uma coleção oficial (digite o código)|Una colección oficial (escribe su código)
Cube from the clipboard (one card per line)|Cubo da área de transferência (uma carta por linha)|Cubo del portapapeles (una carta por línea)
Copy a cube list (one card per line) first.|Copie antes uma lista de cubo (uma carta por linha).|Copia primero una lista de cubo (una carta por línea).
Type the set's code first.|Digite antes o código da coleção.|Escribe primero el código de la colección.
Loading the set from Scryfall|Carregando a coleção do Scryfall|Cargando la colección de Scryfall
Choose what the packs are made of.|Escolha de que são feitos os pacotes.|Elige de qué están hechos los sobres.
A pack needs at least 15 cards to choose from.|Um pacote precisa de pelo menos 15 cartas para escolher.|Un sobre necesita al menos 15 cartas para elegir.
The cube is small for this many players: some packs will be short.|O cubo é pequeno para tantos jogadores: alguns pacotes virão incompletos.|El cubo es pequeño para tantos jugadores: algunos sobres saldrán incompletos.
Three packs; take a card and pass the rest (left, right, left). Then build 40 cards from what you took.|Três pacotes; pegue uma carta e passe o resto (esquerda, direita, esquerda). Depois monte 40 cartas com o que pegou.|Tres sobres; toma una carta y pasa el resto (izquierda, derecha, izquierda). Luego construye 40 cartas con lo que tomaste.
Everyone opens six packs and builds 40 cards from them.|Todos abrem seis pacotes e montam 40 cartas com eles.|Todos abren seis sobres y construyen 40 cartas con ellos.
Filter: name, type, rarity|Filtro: nome, tipo, raridade|Filtro: nombre, tipo, rareza
ALL SHOWN IN|INCLUIR AS VISÍVEIS|INCLUIR LAS VISIBLES
ALL SHOWN OUT|EXCLUIR AS VISÍVEIS|EXCLUIR LAS VISIBLES
cards in packs|cartas nos pacotes|cartas en los sobres
Draft complete|Draft concluído|Draft completado
Pack|Pacote|Sobre
Pick|Escolha|Elección
passing|passando para a|pasando a la
left|esquerda|izquierda
right|direita|derecha
Waiting for the next pack|Esperando o próximo pacote|Esperando el siguiente sobre
Build your deck|Monte seu deck|Construye tu mazo
YOUR PICKS|SUAS ESCOLHAS|TUS ELECCIONES
YOUR CARDS|SUAS CARTAS|TUS CARTAS
Leave the draft?|Sair do draft?|¿Salir del draft?
Your picks so far are lost.|As escolhas feitas até agora serão perdidas.|Las elecciones hechas hasta ahora se perderán.
Leave|Sair|Salir
Keep drafting|Continuar o draft|Seguir con el draft
OPENED CARDS|CARTAS ABERTAS|CARTAS ABIERTAS
Limited|Limitado|Limitado
Draft and sealed: open packs from a real set, a custom set or a cube, then build 40 cards from what you got.|Draft e selado: abra pacotes de uma coleção oficial, de uma coleção custom ou de um cubo e monte 40 cartas com o que saiu.|Draft y sellado: abre sobres de una colección oficial, una colección custom o un cubo, y construye 40 cartas con lo que salió.
Draft / Sealed|Draft / Selado|Draft / Sellado
WITH FRIENDS (LAN)|COM AMIGOS (LAN)|CON AMIGOS (LAN)
MY CUBES|MEUS CUBOS|MIS CUBOS
No cubes yet. Import a .txt list or paste one (one card per line).|Nenhum cubo ainda. Importe uma lista .txt ou cole uma (uma carta por linha).|Aún no hay cubos. Importa una lista .txt o pega una (una carta por línea).
Import a cube|Importar um cubo|Importar un cubo
PASTE A LIST|COLAR UMA LISTA|PEGAR UNA LISTA
FROM A DECK|DE UM DECK|DE UN MAZO
KEEP A DECK AS A CUBE|USAR UM DECK COMO CUBO|USAR UN MAZO COMO CUBO
Delete this cube?|Excluir este cubo?|¿Eliminar este cubo?
Cube saved|Cubo salvo|Cubo guardado
No deck with 45 or more cards.|Nenhum deck com 45 cartas ou mais.|Ningún mazo con 45 cartas o más.
NEW · LIMITED|NOVO · LIMITADO|NUEVO · LIMITADO
Draft and Sealed|Draft e Selado|Draft y Sellado
Open packs from an official set, your custom sets or a cube. Draft against bots or with friends on your network, then build 40 cards from what you opened.|Abra pacotes de uma coleção oficial, das suas coleções custom ou de um cubo. Faça draft contra bots ou com amigos na sua rede e monte 40 cartas com o que abriu.|Abre sobres de una colección oficial, tus colecciones custom o un cubo. Haz draft contra bots o con amigos en tu red y construye 40 cartas con lo que abriste.
<b>Draft:</b> three packs of 15. Take one card, pass the rest; bots fill the empty seats.|<b>Draft:</b> três pacotes de 15. Pegue uma carta e passe o resto; bots completam os lugares vazios.|<b>Draft:</b> tres sobres de 15. Toma una carta y pasa el resto; los bots ocupan los asientos vacíos.
<b>Sealed:</b> six packs each, all yours.|<b>Selado:</b> seis pacotes para cada um, todos seus.|<b>Sellado:</b> seis sobres para cada uno, todos tuyos.
Packs can come from an official set (by its code), a custom set from Magic Set Editor, or one of your cubes. Then build 40 cards from what you opened and play them.|Os pacotes podem vir de uma coleção oficial (pelo código), de uma coleção custom do Magic Set Editor ou de um dos seus cubos. Depois monte 40 cartas com o que abriu e jogue.|Los sobres pueden venir de una colección oficial (por su código), de una colección custom de Magic Set Editor o de uno de tus cubos. Luego construye 40 cartas con lo que abriste y juega.
<b>With friends:</b> host a LAN table and press DRAFT / SEALED in the lobby.|<b>Com amigos:</b> abra uma mesa LAN e aperte DRAFT / SELADO no lobby.|<b>Con amigos:</b> abre una mesa LAN y pulsa DRAFT / SELLADO en el lobby.
<b>1. Set up.</b> Press Draft / Sealed. For each pack choose what it's made of: an official set (type its code, like MKM; cards and rarities come from Scryfall), one of your custom sets, or a cube. CARDS… picks which cards can be opened.|<b>1. Prepare.</b> Aperte Draft / Selado. Para cada pacote escolha de onde ele vem: uma coleção oficial (digite o código, como MKM; cartas e raridades vêm do Scryfall), uma das suas coleções custom ou um cubo. CARTAS… escolhe quais cartas podem sair.|<b>1. Prepara.</b> Pulsa Draft / Sellado. Para cada sobre elige de dónde sale: una colección oficial (escribe su código, como MKM; cartas y rarezas vienen de Scryfall), una de tus colecciones custom o un cubo. CARTAS… elige qué cartas pueden salir.
<b>2. Packs.</b> Sets with rarities open like real boosters: 1 rare (sometimes a mythic), 3 uncommons, 10 commons. Cubes give 15 different cards, and each cube card is opened once per event.|<b>2. Pacotes.</b> Coleções com raridade abrem como boosters de verdade: 1 rara (às vezes mítica), 3 incomuns, 10 comuns. Cubos dão 15 cartas diferentes, e cada carta do cubo sai uma vez só por evento.|<b>2. Sobres.</b> Las colecciones con rareza se abren como sobres de verdad: 1 rara (a veces mítica), 3 infrecuentes, 10 comunes. Los cubos dan 15 cartas distintas, y cada carta del cubo sale una sola vez por evento.
<b>3. Draft.</b> Click a card, click it again (or PICK) to take it. Packs go left, right, left. Bots pick good cards and stay in their colours.|<b>3. Draft.</b> Clique numa carta e clique de novo (ou em ESCOLHA) para pegá-la. Os pacotes vão para a esquerda, direita, esquerda. Os bots pegam boas cartas e ficam nas suas cores.|<b>3. Draft.</b> Haz clic en una carta y otra vez (o en ELECCIÓN) para tomarla. Los sobres van a la izquierda, derecha, izquierda. Los bots eligen buenas cartas y se quedan en sus colores.
<b>4. Build.</b> BUILD YOUR DECK opens the builder with only the cards you opened (the diamonds show how many) plus all the basic lands you want. Make 40 cards, then play it against Forge or at the table.|<b>4. Monte.</b> MONTE SEU DECK abre o editor só com as cartas que você abriu (os losangos mostram quantas) e quantos terrenos básicos quiser. Faça 40 cartas e jogue contra o Forge ou na mesa.|<b>4. Construye.</b> CONSTRUYE TU MAZO abre el editor solo con las cartas que abriste (los rombos muestran cuántas) y todas las tierras básicas que quieras. Haz 40 cartas y juega contra Forge o en la mesa.
<b>Cubes</b> are lists, one card per line. Import a .txt, paste one, or keep any deck of 45+ cards as a cube.|<b>Cubos</b> são listas, uma carta por linha. Importe um .txt, cole uma lista ou use qualquer deck de 45+ cartas como cubo.|<b>Los cubos</b> son listas, una carta por línea. Importa un .txt, pega una o usa cualquier mazo de 45+ cartas como cubo.
# ---------------------------------------------------------------- translation pass 2
that card|essa carta|esa carta
out|fora|fuera
heads|cara|cara
tails|coroa|cruz
its back|o verso|su reverso
it|ela|ella
→ bottom of library|→ fundo do grimório|→ fondo de la biblioteca
→ top of library|→ topo do grimório|→ parte superior de la biblioteca
→ battlefield|→ campo de batalha|→ campo de batalla
→ graveyard|→ cemitério|→ cementerio
→ exile|→ exílio|→ exilio
→ hand|→ mão|→ mano
→ command|→ zona de comando|→ zona de mando
Someone|Alguém|Alguien
Set code (e.g. MKM, DSK, FDN):|Código da coleção (ex.: MKM, DSK, FDN):|Código de la colección (p. ej. MKM, DSK, FDN):
A real set (type its code)…|Uma coleção oficial (digite o código)…|Una colección oficial (escribe su código)…
Clipboard|Área de transferência|Portapapeles
No packs or no players.|Sem pacotes ou sem jogadores.|No hay sobres o no hay jugadores.
Scryfall sent something unreadable.|O Scryfall mandou algo ilegível.|Scryfall envió algo ilegible.
In Magic Set Editor use File → Export → Cockatrice, then import the .xml here. Card images from the export are found and copied automatically. Custom cards appear in deck builder searches and play at the kitchen table (Forge rules games use real cards only).|No Magic Set Editor use Arquivo → Exportar → Cockatrice e importe o .xml aqui. As imagens das cartas exportadas são encontradas e copiadas automaticamente. As cartas custom aparecem nas buscas do editor de decks e na mesa da cozinha (partidas com as regras do Forge usam só cartas reais).|En Magic Set Editor usa Archivo → Exportar → Cockatrice e importa el .xml aquí. Las imágenes exportadas se encuentran y copian automáticamente. Las cartas custom aparecen en las búsquedas del editor de mazos y en la mesa de cocina (las partidas con reglas de Forge usan solo cartas reales).
<color=#f7a540>Forge isn't running: start it in the Forge tab to host.</color>|<color=#f7a540>O Forge não está rodando: inicie-o na aba Forge para hospedar.</color>|<color=#f7a540>Forge no está en marcha: inícialo en la pestaña Forge para organizar.</color>
<size=15>Friends open PLAY ON LAN → JOIN A TABLE (Forge mode) and pick you, or type this address.</size>|<size=15>Os amigos abrem JOGAR NA LAN → ENTRAR NUMA MESA (modo Forge) e escolhem você, ou digitam este endereço.</size>|<size=15>Tus amigos abren JUGAR EN LAN → UNIRSE A UNA MESA (modo Forge) y te eligen, o escriben esta dirección.</size>
<size=15>Other players open JOIN A TABLE and pick you from the list, or type this address.</size>|<size=15>Os outros abrem ENTRAR NUMA MESA e escolhem você na lista, ou digitam este endereço.</size>|<size=15>Los demás abren UNIRSE A UNA MESA y te eligen de la lista, o escriben esta dirección.</size>
1.  The host|1.  O anfitrião|1.  El anfitrión
Import custom sets from Magic Set Editor and playtest them at a free table, like Cockatrice: alone with several seats, or with friends over your local network. No rules engine and no Forge needed.|Importe coleções custom do Magic Set Editor e teste-as numa mesa livre, como no Cockatrice: sozinho com vários lugares ou com amigos pela rede local. Sem motor de regras e sem precisar do Forge.|Importa colecciones custom de Magic Set Editor y pruébalas en una mesa libre, como en Cockatrice: solo con varios asientos o con amigos en tu red local. Sin motor de reglas y sin Forge.
<b>No rules engine.</b> Unlike Forge games, the table doesn't know the rules: you play the cards like on a real table. That's what lets it use cards that don't exist anywhere else.|<b>Sem motor de regras.</b> Diferente das partidas do Forge, a mesa não conhece as regras: você joga as cartas como numa mesa de verdade. É isso que permite usar cartas que não existem em nenhum outro lugar.|<b>Sin motor de reglas.</b> A diferencia de las partidas de Forge, la mesa no conoce las reglas: juegas las cartas como en una mesa de verdad. Eso permite usar cartas que no existen en ningún otro sitio.
<b>Start a table:</b> on Home, set the mode to KITCHEN TABLE, pick your deck and press Play. Each other seat gets a deck (CHOOSE THEIR DECKS picks them, great for goldfishing a matchup). Ctrl + click a seat's nameplate to play from their side.|<b>Começar uma mesa:</b> no Início, ponha o modo em MESA DA COZINHA, escolha seu deck e aperte Jogar. Cada outro lugar recebe um deck (ESCOLHER OS DECKS DELES escolhe, ótimo para testar um confronto). Ctrl + clique na placa de nome de um lugar para jogar pelo lado dele.|<b>Empezar una mesa:</b> en Inicio, pon el modo en MESA DE COCINA, elige tu mazo y pulsa Jugar. Cada otro asiento recibe un mazo (ELEGIR SUS MAZOS los elige, ideal para probar un enfrentamiento). Ctrl + clic en la placa de un asiento para jugar desde su lado.
<b>Moving cards:</b> drag cards anywhere on your battlefield, into your hand, onto a pile, or to another player. Drag a pile to take its top card. Click to tap, double-click a hand card to play it, right-click anything for every action. Hold Alt to zoom, Q / E to turn a card, F to transform double-faced cards. Ctrl + Z undoes.|<b>Mover cartas:</b> arraste cartas para qualquer lugar do seu campo de batalha, para a mão, para uma pilha ou para outro jogador. Arraste uma pilha para pegar a carta do topo. Clique para virar, clique duas vezes numa carta da mão para jogá-la, botão direito em qualquer coisa mostra todas as ações. Segure Alt para ampliar, Q / E para girar uma carta, F para transformar cartas de duas faces. Ctrl + Z desfaz.|<b>Mover cartas:</b> arrastra cartas a cualquier parte de tu campo de batalla, a tu mano, a una pila o a otro jugador. Arrastra una pila para tomar la carta de arriba. Clic para girar, doble clic en una carta de la mano para jugarla, clic derecho en cualquier cosa para todas las acciones. Mantén Alt para ampliar, Q / E para girar una carta, F para transformar cartas de dos caras. Ctrl + Z deshace.
<b>With friends:</b> PLAY ON LAN (HOST / JOIN) on Home. Everyone on the same network; custom sets are copied to the others automatically, so everybody plays with the same cards.|<b>Com amigos:</b> JOGAR NA LAN (HOSPEDAR / ENTRAR) no Início. Todos na mesma rede; as coleções custom são copiadas para os outros automaticamente, então todos jogam com as mesmas cartas.|<b>Con amigos:</b> JUGAR EN LAN (ORGANIZAR / UNIRSE) en Inicio. Todos en la misma red; las colecciones custom se copian a los demás automáticamente, así que todos juegan con las mismas cartas.
<b>Decks:</b> build them in Decks like any other. Set the format (Commander, 60 or 40 cards) and the card pool (your custom sets, real cards, or both).|<b>Decks:</b> monte-os em Decks como qualquer outro. Defina o formato (Commander, 60 ou 40 cartas) e o pool de cartas (suas coleções custom, cartas reais ou ambas).|<b>Mazos:</b> constrúyelos en Mazos como cualquier otro. Elige el formato (Commander, 60 o 40 cartas) y el pool de cartas (tus colecciones custom, cartas reales o ambas).
<b>1.</b>  In Magic Set Editor, open your set and choose <b>File → Export → Cockatrice</b>. Keep the card images it exports in the same folder as the .xml (or a folder inside it).|<b>1.</b>  No Magic Set Editor, abra sua coleção e escolha <b>Arquivo → Exportar → Cockatrice</b>. Deixe as imagens exportadas na mesma pasta do .xml (ou numa pasta dentro dela).|<b>1.</b>  En Magic Set Editor, abre tu colección y elige <b>Archivo → Exportar → Cockatrice</b>. Deja las imágenes exportadas en la misma carpeta que el .xml (o en una carpeta dentro).
<b>2.</b>  Press <b>Import a set</b> below and choose that .xml file. MTG United copies the cards and finds their images by name.|<b>2.</b>  Aperte <b>Importar uma coleção</b> abaixo e escolha esse .xml. O MTG United copia as cartas e encontra as imagens pelo nome.|<b>2.</b>  Pulsa <b>Importar una colección</b> abajo y elige ese .xml. MTG United copia las cartas y encuentra sus imágenes por nombre.
<b>3.</b>  In Decks, open a deck, set <b>POOL</b> to your set, and build. Search works with Scryfall words too (t:creature c:r mv<=3).|<b>3.</b>  Em Decks, abra um deck, ponha o <b>POOL</b> na sua coleção e monte. A busca também aceita palavras do Scryfall (t:creature c:r mv<=3).|<b>3.</b>  En Mazos, abre un mazo, pon el <b>POOL</b> en tu colección y construye. La búsqueda también acepta palabras de Scryfall (t:creature c:r mv<=3).
<b>4.</b>  Play it here at the kitchen table. Tokens and double-faced cards from the set work: right-click a card to create its tokens, press F to transform.|<b>4.</b>  Jogue aqui na mesa da cozinha. Fichas e cartas de duas faces da coleção funcionam: botão direito numa carta cria as fichas dela, F transforma.|<b>4.</b>  Juega aquí en la mesa de cocina. Las fichas y cartas de dos caras de la colección funcionan: clic derecho en una carta crea sus fichas, F transforma.
<color=#9aa0aa>No custom sets yet.</color>|<color=#9aa0aa>Nenhuma coleção custom ainda.</color>|<color=#9aa0aa>Aún no hay colecciones custom.</color>
<color=#5fd47a>●</color>  Connected and ready|<color=#5fd47a>●</color>  Conectado e pronto|<color=#5fd47a>●</color>  Conectado y listo
<color=#e55>●</color>  Not installed. Set it up in the Forge tab|<color=#e55>●</color>  Não instalado. Configure na aba Forge|<color=#e55>●</color>  No instalado. Configúralo en la pestaña Forge
<color=#f7a540>●</color>  Starting (loading cards)...|<color=#f7a540>●</color>  Iniciando (carregando cartas)...|<color=#f7a540>●</color>  Iniciando (cargando cartas)...
<color=#9aa0aa>●</color>  Installed, not running|<color=#9aa0aa>●</color>  Instalado, parado|<color=#9aa0aa>●</color>  Instalado, detenido
<color=#5fd47a>●</color>  Running and connected|<color=#5fd47a>●</color>  Rodando e conectado|<color=#5fd47a>●</color>  En marcha y conectado
<color=#e55>●</color>  Not installed|<color=#e55>●</color>  Não instalado|<color=#e55>●</color>  No instalado
<color=#f7a540>your turn</color>|<color=#f7a540>seu turno</color>|<color=#f7a540>tu turno</color>
Using MTG United's copy of Forge.|Usando a cópia do Forge do MTG United.|Usando la copia de Forge de MTG United.
Space / Enter|Espaço / Enter|Espacio / Enter
Space|Espaço|Espacio
` or 0|` ou 0|` o 0
(when the mouse isn't over a card or pile)|(quando o mouse não está sobre uma carta ou pilha)|(cuando el ratón no está sobre una carta o pila)
(the card under the mouse, or every selected card)|(a carta sob o mouse, ou todas as cartas selecionadas)|(la carta bajo el ratón, o todas las cartas seleccionadas)
(the mouse over your library, graveyard, exile...)|(o mouse sobre seu grimório, cemitério, exílio...)|(el ratón sobre tu biblioteca, cementerio, exilio...)
A draft / sealed deck uses the cards you opened, plus basic lands.|Um deck de draft / selado usa as cartas que você abriu, mais terrenos básicos.|Un mazo de draft / sellado usa las cartas que abriste, más tierras básicas.
   ·   <color=#f7a540>no commander yet</color>|   ·   <color=#f7a540>sem comandante ainda</color>|   ·   <color=#f7a540>aún sin comandante</color>
MTG United plays Commander with the rules engine of Forge, the open-source Magic program. This is where you set it up.|O MTG United joga Commander com o motor de regras do Forge, o programa de Magic de código aberto. É aqui que você o configura.|MTG United juega Commander con el motor de reglas de Forge, el programa de Magic de código abierto. Aquí lo configuras.
ACTIONS|AÇÕES|ACCIONES
New here? Press ""How to set up Forge"" (top right) for a step-by-step guide. Already have Forge? Use ""Use my Forge..."" or ""Find Forge"" so nothing is downloaded twice. Otherwise ""Install Forge"" downloads about 500 MB and builds it (a few minutes).|Primeira vez? Aperte ""Como configurar o Forge"" (canto superior direito) para um guia passo a passo. Já tem o Forge? Use ""Usar meu Forge..."" ou ""Encontrar o Forge"" para não baixar nada duas vezes. Senão, ""Instalar Forge"" baixa cerca de 500 MB e compila (alguns minutos).|¿Primera vez? Pulsa ""Cómo configurar Forge"" (arriba a la derecha) para una guía paso a paso. ¿Ya tienes Forge? Usa ""Usar mi Forge..."" o ""Buscar Forge"" para no descargar nada dos veces. Si no, ""Instalar Forge"" descarga unos 500 MB y lo compila (unos minutos).
MTG United builds its bridge against your Forge and reads its card data through a link. Your Forge folder and your desktop Forge settings are never changed.|O MTG United compila a ponte para o seu Forge e lê os dados das cartas por um link. A pasta do seu Forge e as configurações do Forge desktop nunca são alteradas.|MTG United compila su puente para tu Forge y lee los datos de las cartas mediante un enlace. Tu carpeta de Forge y los ajustes de Forge de escritorio nunca se modifican.
•  <b>""Doesn't compile against this Forge version""</b>: your Forge is too different from the one MTG United was made for. Use <b>Install Forge</b> instead.|•  <b>""Não compila com esta versão do Forge""</b>: seu Forge é muito diferente daquele para o qual o MTG United foi feito. Use <b>Instalar Forge</b>.|•  <b>""No compila con esta versión de Forge""</b>: tu Forge es muy distinto del que MTG United espera. Usa <b>Instalar Forge</b>.
(Forge isn't running: only custom sets searched)|(o Forge não está rodando: só coleções custom foram buscadas)|(Forge no está en marcha: solo se buscaron colecciones custom)
the host picks the format|o anfitrião escolhe o formato|el anfitrión elige el formato
in a game|em partida|en partida
in progress|em andamento|en curso
host|anfitrião|anfitrión
Working... you can keep using the menus; output is shown below.|Trabalhando... você pode continuar usando os menus; a saída aparece abaixo.|Trabajando... puedes seguir usando los menús; la salida se muestra abajo.
1. Install Java (JDK 21)|1. Instalar o Java (JDK 21)|1. Instalar Java (JDK 21)
No Forge installs found.|Nenhuma instalação do Forge encontrada.|No se encontró ninguna instalación de Forge.
MTG United is the table: the cards, animations, sounds and menus.|O MTG United é a mesa: as cartas, animações, sons e menus.|MTG United es la mesa: las cartas, animaciones, sonidos y menús.
The rules of Magic (tens of thousands of cards, the stack, combat, every Commander rule) and the AI opponents come from <b>Forge</b>, a free, open-source Magic program. MTG United runs Forge in the background through a small helper called the <b>bridge</b>, and draws what Forge decides.|As regras do Magic (dezenas de milhares de cartas, a pilha, o combate, todas as regras de Commander) e os oponentes da IA vêm do <b>Forge</b>, um programa de Magic gratuito e de código aberto. O MTG United roda o Forge em segundo plano por meio de um pequeno ajudante chamado <b>ponte</b>, e desenha o que o Forge decide.|Las reglas de Magic (decenas de miles de cartas, la pila, el combate, todas las reglas de Commander) y los oponentes de la IA vienen de <b>Forge</b>, un programa de Magic gratuito y de código abierto. MTG United ejecuta Forge en segundo plano mediante un pequeño ayudante llamado <b>puente</b>, y dibuja lo que Forge decide.
So before your first game, Forge has to be on your computer once.|Então, antes da primeira partida, o Forge precisa estar no seu computador uma vez.|Así que, antes de tu primera partida, Forge tiene que estar en tu ordenador una vez.
•  <b>Java 17 or newer (the JDK)</b>. Forge is written in Java. If the Forge tab says Java is ""not found"", open a terminal and run:|•  <b>Java 17 ou mais novo (o JDK)</b>. O Forge é escrito em Java. Se a aba Forge disser que o Java ""não foi encontrado"", abra um terminal e rode:|•  <b>Java 17 o más nuevo (el JDK)</b>. Forge está escrito en Java. Si la pestaña Forge dice que Java ""no se encuentra"", abre una terminal y ejecuta:
   (or press <b>Install Java</b> below), then restart MTG United.|   (ou aperte <b>Instalar Java</b> abaixo), depois reinicie o MTG United.|   (o pulsa <b>Instalar Java</b> abajo), luego reinicia MTG United.
•  <b>Git</b>, only if MTG United installs Forge for you (it downloads Forge's source). <b>Install Git</b> below does it.|•  <b>Git</b>, só se o MTG United for instalar o Forge para você (ele baixa o código do Forge). <b>Instalar Git</b> abaixo faz isso.|•  <b>Git</b>, solo si MTG United instala Forge por ti (descarga el código de Forge). <b>Instalar Git</b> abajo lo hace.
•  <b>Disk space</b>: about 1.5 GB if MTG United installs Forge for you; almost nothing if you already have Forge.|•  <b>Espaço em disco</b>: cerca de 1,5 GB se o MTG United instalar o Forge para você; quase nada se você já tem o Forge.|•  <b>Espacio en disco</b>: unos 1,5 GB si MTG United instala Forge por ti; casi nada si ya tienes Forge.
•  <b>Internet</b> for the first install, and for card images while you play.|•  <b>Internet</b> para a primeira instalação e para as imagens das cartas enquanto você joga.|•  <b>Internet</b> para la primera instalación y para las imágenes de las cartas mientras juegas.
If Forge is already installed (or unzipped) on your PC, you don't need to download it again.|Se o Forge já está instalado (ou descompactado) no seu PC, você não precisa baixá-lo de novo.|Si Forge ya está instalado (o descomprimido) en tu PC, no hace falta descargarlo otra vez.
•  <b>Find Forge</b> looks in the usual places (Program Files, Desktop, Downloads, Documents, Games...).|•  <b>Encontrar o Forge</b> procura nos lugares de sempre (Arquivos de Programas, Área de Trabalho, Downloads, Documentos, Jogos...).|•  <b>Buscar Forge</b> mira en los sitios habituales (Archivos de programa, Escritorio, Descargas, Documentos, Juegos...).
•  <b>Use my Forge...</b> lets you pick the folder yourself: the one that contains <b>forge.exe</b> (or Forge's .jar) and a <b>res</b> folder.|•  <b>Usar meu Forge...</b> deixa você escolher a pasta: a que contém o <b>forge.exe</b> (ou o .jar do Forge) e uma pasta <b>res</b>.|•  <b>Usar mi Forge...</b> te deja elegir la carpeta: la que contiene <b>forge.exe</b> (o el .jar de Forge) y una carpeta <b>res</b>.
Press <b>Install Forge</b>. MTG United will:|Aperte <b>Instalar Forge</b>. O MTG United vai:|Pulsa <b>Instalar Forge</b>. MTG United va a:
   1.  download Forge's source at the version it was made for (about 500 MB, mostly card scripts);|   1.  baixar o código do Forge na versão para a qual foi feito (cerca de 500 MB, quase tudo scripts de cartas);|   1.  descargar el código de Forge en la versión para la que fue hecho (unos 500 MB, casi todo scripts de cartas);
   2.  build Forge's rules engine (a few minutes the first time);|   2.  compilar o motor de regras do Forge (alguns minutos na primeira vez);|   2.  compilar el motor de reglas de Forge (unos minutos la primera vez);
   3.  build the bridge.|   3.  compilar a ponte.|   3.  compilar el puente.
You can watch every step in the <b>Output</b> box at the bottom of this tab. Everything goes into MTG United's own folder.|Você pode acompanhar cada passo na caixa <b>Saída</b> no fim desta aba. Tudo vai para a pasta do próprio MTG United.|Puedes ver cada paso en el cuadro <b>Salida</b> al final de esta pestaña. Todo va a la carpeta del propio MTG United.
When setup finishes, the bridge starts by itself. The dot at the top of this tab (and on Home) tells you where it is:|Quando a configuração termina, a ponte inicia sozinha. A bolinha no topo desta aba (e no Início) mostra como ela está:|Cuando termina la configuración, el puente arranca solo. El punto arriba de esta pestaña (y en Inicio) indica cómo está:
   <color=#5fd47a>●</color>  Running and connected: ready, press Play on Home.|   <color=#5fd47a>●</color>  Rodando e conectado: pronto, aperte Jogar no Início.|   <color=#5fd47a>●</color>  En marcha y conectado: listo, pulsa Jugar en Inicio.
   <color=#f7a540>●</color>  Starting: Forge is loading every card, about 30 to 60 seconds.|   <color=#f7a540>●</color>  Iniciando: o Forge está carregando todas as cartas, cerca de 30 a 60 segundos.|   <color=#f7a540>●</color>  Iniciando: Forge está cargando todas las cartas, unos 30 a 60 segundos.
   <color=#9aa0aa>●</color>  Installed, not running: press Start bridge.|   <color=#9aa0aa>●</color>  Instalado, parado: aperte Iniciar ponte.|   <color=#9aa0aa>●</color>  Instalado, detenido: pulsa Iniciar puente.
   <color=#e55>●</color>  Not installed: go back a step.|   <color=#e55>●</color>  Não instalado: volte um passo.|   <color=#e55>●</color>  No instalado: vuelve un paso.
•  <b>Java not found</b>: install the JDK (step 2) and restart MTG United.|•  <b>Java não encontrado</b>: instale o JDK (passo 2) e reinicie o MTG United.|•  <b>Java no encontrado</b>: instala el JDK (paso 2) y reinicia MTG United.
•  <b>Stuck or crashing</b>: <b>Open log</b> shows the bridge's log; <b>Restart bridge</b> often helps; <b>Reinstall</b> rebuilds everything.|•  <b>Travado ou fechando</b>: <b>Abrir log</b> mostra o log da ponte; <b>Reiniciar ponte</b> costuma resolver; <b>Reinstalar</b> refaz tudo.|•  <b>Bloqueado o cerrándose</b>: <b>Abrir registro</b> muestra el registro del puente; <b>Reiniciar puente</b> suele ayudar; <b>Reinstalar</b> lo reconstruye todo.
•  <b>Updating MTG United</b>: press <b>Update</b> afterwards so the bridge is rebuilt with the new version.|•  <b>Atualizando o MTG United</b>: aperte <b>Atualizar</b> depois, para a ponte ser recompilada com a nova versão.|•  <b>Actualizando MTG United</b>: pulsa <b>Actualizar</b> después para que el puente se recompile con la nueva versión.
•  <b>Uninstall</b> removes only MTG United's parts. With ""Keep settings & log"" on, your bridge settings survive.|•  <b>Desinstalar</b> remove só as partes do MTG United. Com ""Manter configurações e log"" ligado, as configurações da ponte continuam.|•  <b>Desinstalar</b> quita solo las partes de MTG United. Con ""Conservar ajustes y registro"" activado, los ajustes del puente se mantienen.
""4 Lightning Bolt"" lines; the sideboard after a blank line (commander there for Commander decks).|Linhas como ""4 Lightning Bolt""; o sideboard depois de uma linha em branco (o comandante fica lá em decks de Commander).|Líneas como ""4 Lightning Bolt""; el banquillo después de una línea en blanco (el comandante va ahí en mazos de Commander).
Export → Copy. The [Commander] category becomes the commander; maybeboard is left out.|Exportar → Copiar. A categoria [Commander] vira o comandante; o maybeboard fica de fora.|Exportar → Copiar. La categoría [Commander] se convierte en el comandante; el maybeboard se deja fuera.
<color=#f7a540>Your clipboard is empty: copy a deck list first.</color>|<color=#f7a540>Sua área de transferência está vazia: copie uma lista de deck primeiro.</color>|<color=#f7a540>Tu portapapeles está vacío: copia primero una lista de mazo.</color>
this card targets that one|esta carta mira aquela|esta carta apunta a aquella
Roll d6|Rolar d6|Tirar d6
Roll d20|Rolar d20|Tirar d20
Flip a coin|Jogar uma moeda|Lanzar una moneda
Choose a random player|Escolher um jogador aleatório|Elegir un jugador aleatorio
Mill X|Moer X|Moler X
Shuffle all into library|Embaralhar tudo no grimório|Barajar todo en la biblioteca
Turn left|Girar para a esquerda|Girar a la izquierda
Turn right|Girar para a direita|Girar a la derecha
+1/+1 counter|Marcador +1/+1|Contador +1/+1
−1/−1 counter|Marcador −1/−1|Contador −1/−1
Add a counter|Adicionar um marcador|Añadir un contador
Remove a counter|Remover um marcador|Quitar un contador
Copy (token)|Copiar (ficha)|Copiar (ficha)
Gone|Saiu|Fuera
+1 poison|+1 veneno|+1 veneno
−1 poison|−1 veneno|−1 veneno
Set life to 20|Vida em 20|Vida a 20
Set life to 40|Vida em 40|Vida a 40
Name|Nome|Nombre
Installing Forge|Instalando o Forge|Instalando Forge
Reinstalling Forge|Reinstalando o Forge|Reinstalando Forge
Updating Forge|Atualizando o Forge|Actualizando Forge
Setting up your Forge|Configurando o seu Forge|Configurando tu Forge
Uninstalling|Desinstalando|Desinstalando
Installing Java|Instalando o Java|Instalando Java
Installing Git|Instalando o Git|Instalando Git
# ---------------------------------------------------------------- errors
Something went wrong|Algo deu errado|Algo salió mal
Details saved in Options → Error log.|Detalhes salvos em Opções → Log de erros.|Detalles guardados en Opciones → Registro de errores.
Couldn't read or write a file (is it open in another program?).|Não foi possível ler ou gravar um arquivo (ele está aberto em outro programa?).|No se pudo leer o escribir un archivo (¿está abierto en otro programa?).
A file MTG United needs is missing.|Está faltando um arquivo de que o MTG United precisa.|Falta un archivo que MTG United necesita.
Network problem: check that both PCs are on the same network.|Problema de rede: confira se os PCs estão na mesma rede.|Problema de red: comprueba que los PCs están en la misma red.
The game ran out of memory. Try a smaller card size or restart.|O jogo ficou sem memória. Tente cartas menores ou reinicie.|El juego se quedó sin memoria. Prueba cartas más pequeñas o reinicia.
Something went wrong, but you can keep playing.|Algo deu errado, mas você pode continuar jogando.|Algo salió mal, pero puedes seguir jugando.
MTG United didn't close normally last time.|O MTG United não fechou normalmente da última vez.|MTG United no se cerró con normalidad la última vez.
No errors so far.|Nenhum erro até agora.|Ningún error por ahora.
";
    }
}
