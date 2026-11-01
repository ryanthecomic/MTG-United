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
