# Histórico de versões

## 1.7.4 — 20/09/2026

- O GameHub foi remodelado em torno de uma **faixa de destaques** acima da biblioteca: cartões largos e arredondados que alternam entre **Recentes**, **Favoritos** e **Mais jogados**. A grade de capas abaixo ficou compacta, assumindo o papel de catálogo.
- O destaque mostra o estado da máquina — **CPU, GPU e RAM ao vivo** — e o **FPS médio real** que aquele jogo alcançou nas sessões registradas.
- Clicar em **Perfil** agora lista os perfis para escolher, com o atual marcado, em vez de abrir direto o editor. Mudar o que um perfil faz continua acessível, no rodapé dessa lista.
- O seletor do controle percorre a faixa de destaques: as setas laterais andam entre os cartões e as verticais entram e saem da faixa.
- Botões com gradiente e profundidade: o Jogar ganhou volume e brilho na cor da marca, e as superfícies de vidro substituíram os blocos chapados.

## 1.7.3 — 20/09/2026

- Os jogos que ficaram com o **quadrado colorido no lugar da capa** agora são tentados de novo sozinhos ao abrir o GameHub, uma vez por sessão. Antes a correção existia mas dependia de acionar "Procurar capas que faltam" no menu, e quem não soubesse disso continuava vendo a capa antiga.
- Isso alcança os jogos ilustrados antes da correção de capas da Steam — EA SPORTS FC 26, PEAK e afins, cujo placeholder tinha sido gravado por uma versão que ainda não sabia onde procurar.

## 1.7.2 — 20/09/2026

- **PCSX2 (PlayStation 2) e PPSSPP (PSP)** entram na lista de emuladores do cadastro, ao lado dos de Switch. Escolher um já define extensões, argumentos e plataforma, e as ROMs dos dois consoles já ganham capa pelo acervo de arte.
- O PCSX2 é cadastrado com `-batch`: sem ele o emulador volta para a própria interface ao sair da partida, e o GameHub continuaria achando que a sessão está em andamento. O PPSSPP recebe `--escape-exit`, para o Esc fechar o jogo sem precisar de teclado extra.
- Os demais emuladores do catálogo (Dolphin, RPCS3, RetroArch, Cemu, DuckStation e outros) continuam sendo reconhecidos pelo executável em "Outro emulador".

## 1.7.1 — 20/09/2026

- **ROMs de Switch agora têm capa.** A arte vem do catálogo da eShop (nome, ícone e banner oficiais de cada título), que cobre o console inteiro — inclusive os exclusivos, que não existem em loja de PC. O catálogo é lido em fluxo, então a consulta custa poucos megabytes de memória e leva segundos na primeira ROM.
- **Corrigidos os jogos da Steam que ficavam sem capa.** A loja passou a servir a arte dos títulos mais recentes por um endereço com hash, e os caminhos fixos do CDN respondiam 404 — o jogo aparecia com o quadrado colorido mesmo estando na loja normalmente. Agora, quando o caminho fixo falha, o Pulse pergunta à própria loja qual é o endereço da imagem.

## 1.7.0 — 20/09/2026

- As **ROMs agora ganham a capa original do console**, buscada no acervo público do libretro (o mesmo que o RetroArch usa) — a loja da Steam, usada até aqui, não cataloga jogos de console. Cobre Nintendo (NES ao Wii U, Game Boy ao 3DS), PlayStation 1 a 4, PSP, Vita, Sega (Master System ao Dreamcast), Xbox, arcade e outros.
- A busca entende as diferenças entre o nome do arquivo e o do acervo: região no nome (`Metroid Prime (USA)`), artigo no fim (`Legend of Zelda, The`) e pontuação trocada. Quando a confiança é baixa, prefere não ilustrar a ilustrar errado.
- Novo item no menu do GameHub: **Procurar capas que faltam**. Ele tenta de novo nos jogos que ficaram com o quadrado colorido no lugar da capa — útil quando a varredura anterior rodou sem internet.
- Jogos de Switch continuam dependendo da busca por nome na loja: eles não estão no acervo do libretro, e os exclusivos do console não têm equivalente em loja de PC.

## 1.6.9 — 20/09/2026

- Cada jogo da biblioteca agora possui seu próprio painel de estatísticas, mesmo antes da primeira sessão. Horas, FPS, 1% low, telemetria, gráfico e maior dia ficam isolados por jogo.
- A navegação por gamepad percorre jogos, coleta geral, coletores individuais e ações com um seletor visível. O botão B continua levando o seletor para **Fechar** antes de sair.

## 1.6.8 — 20/09/2026

- O GameHub ganhou um painel de **Estatísticas Pro** acessível por um novo ícone ao lado das ações do jogo. A tela reúne horas jogadas, FPS médio, 1% low, temperaturas e uso médios de CPU/GPU, RAM, sessões e atividade dos últimos 14 dias.
- A telemetria reutiliza os leitores de hardware do próprio Pulse1x, guarda apenas médias locais por sessão e nunca envia dados. Horas, FPS, temperaturas, uso de CPU/GPU e RAM podem ser desligados individualmente.
- A coleta usa amostras espaçadas a cada três segundos e o painel informa claramente o impacto esperado conforme os sensores ativos.
- Toda a nova tela pode ser percorrida e operada por gamepad, incluindo a lista de jogos, os interruptores de coleta, a limpeza do histórico e o fechamento.

## 1.6.7 — 20/09/2026

- O cadastro de emulador não pede mais o **nome**: ele vem do emulador escolhido na lista e, em "Outro emulador", do próprio arquivo. É só um rótulo na biblioteca, e digitá-lo não mudava nada no funcionamento.

## 1.6.6 — 20/09/2026

- Cadastrar um emulador agora é **escolher qual você usa** numa lista — Ryujinx, Eden, Citron ou Yuzu (e forks) — em vez de descobrir a linha de comando dele. Extensões, argumentos e plataforma vêm prontos; basta apontar o executável e a pasta das ROMs.
- Cada emulador mostra **qual arquivo escolher**. No Citron isso resolve a pegadinha que fazia o emulador abrir sem o jogo: quem carrega a ROM pela linha de comando é o `citron-cmd.exe`, não o `citron.exe`.
- Os campos técnicos (nome, extensões, argumentos, plataforma) ficaram **recolhidos em "Configurações avançadas"**. Continuam editáveis para o que o catálogo não cobre, e abrem sozinhos ao escolher "Outro emulador".

## 1.6.5 — 20/09/2026

- O cadastro de emulador agora **reconhece o emulador pelo executável** e preenche sozinho extensões, argumentos e plataforma. Cada emulador tem sua própria sintaxe de linha de comando — Ryujinx aceita o caminho da ROM solto, Eden, Citron e os forks do Yuzu exigem `-g` — e descobrir isso na tentativa e erro era o que fazia a ROM abrir o emulador vazio em vez do jogo.
- Catálogo inicial com Ryujinx (e o fork Ryubing), Eden, Citron, Yuzu e forks (Suyu, Sudachi), RetroArch, Dolphin, PCSX2, RPCS3, PPSSPP, Cemu, Citra e forks (Lime, Azahar), DuckStation, xemu, Vita3K e melonDS. Tudo continua editável: o catálogo é um ponto de partida, não uma imposição.
- **Citron**: o Pulse avisa que é preciso apontar para o `citron-cmd.exe`, e não para o `citron.exe` — só ele carrega a ROM pela linha de comando. Também passa a acompanhar o processo certo (`citron`), para que o perfil de otimização não seja aplicado ao processo errado nem deixe de perceber o jogo fechando.

## 1.6.4 — 19/09/2026

- Corrigidas as fontes que apareciam **pretas sobre o fundo escuro**: o texto das caixas de seleção e dos botões de opção dentro de listas (lista de launchers nas Configurações, opções do editor de perfil, plano de fundo do aplicativo) não herdava a cor do tema.
- As seções recolhíveis das Configurações usavam o tema antigo do Windows, com cabeçalho claro e texto preto. Agora seguem o visual do resto do app.

## 1.6.3 — 19/09/2026

- **Esc abre o menu lateral do GameHub.** O atalho existia, mas só funcionava quando o foco do teclado estava dentro da página — ao entrar no hub ou voltar de uma janela, a tecla não fazia nada.
- A Central Pós-Formatação agora tem **categorias recolhíveis**, com a contagem de itens no cabeçalho. "Drivers e Fabricantes" começa fechada: sozinha, ela empurrava o resto da página para fora da tela.
- O **Debloater** (Detector de Bloatware) passa a aparecer também na Central Pós-Formatação — instalar o que falta e remover o que veio de fábrica são as duas metades de preparar um PC recém-formatado.
  - O catálogo de programas de fabricante foi de 17 para 65 itens (Dell, HP, Lenovo, ASUS, Acer, Samsung, LG, MSI, Gigabyte, Huawei, Positivo, VAIO), além de antivírus e aplicativos promocionais pré-instalados.
  - A desinstalação agora roda **em modo silencioso** quando reconhece o formato do desinstalador (MSI, NSIS, Inno Setup, InstallShield) e, depois, oferece apagar as pastas deixadas para trás. Formatos não reconhecidos continuam abrindo o desinstalador oficial.
- Nova categoria **Hardware (CPU, GPU e RAM)** nas Otimizações Avançadas: agendamento de GPU por hardware, desativar o estacionamento de núcleos, desativar a limitação de energia, interrupções MSI na placa de vídeo e prioridade de jogos no agendador do Windows. Nada faz overclock nem mexe em voltagem — o que muda é como o Windows distribui trabalho ao hardware, e tudo é reversível pelo mesmo histórico das demais otimizações.

## 1.6.2 — 19/09/2026

- A seção **Personalização do Windows** foi adiada e agora exibe apenas um aviso de "em breve". A versão anterior alterava a barra de tarefas e as janelas do Explorer, mas Menu Iniciar, Configurações e as áreas internas do Explorer não são alcançáveis sem injetar código nesses processos — com risco de derrubar o Explorer a cada atualização do Windows. A seção volta quando houver uma base segura para isso.
- Removido junto o subsistema que a sustentava (motor, temas, inicialização automática e o vigia do Explorer). O Pulse não altera mais nada no visual do Windows, e nenhuma configuração dele fica para trás.

## 1.6.1 — 19/09/2026

- A Personalização do Windows passa a deixar escolher **em quais telas** a barra de tarefas recebe o efeito. Num notebook com monitor externo, personalizar as duas raramente é o desejado; desmarcar uma tela a devolve ao visual padrão do Windows. A escolha é guardada pelo nome do monitor, então sobrevive a reinícios do Explorer.
- Corrigido o principal motivo de "apliquei o tema e não mudou nada": com a **ocultação automática** da barra de tarefas ligada, o Windows mantém a barra fora da área da tela — o efeito é aplicado, mas fica invisível. A seção agora detecta isso, explica e oferece um botão que desliga a ocultação.
- A reaplicação periódica não reescreve mais um efeito que a janela já tem. Cada escrita fazia o gerenciador de janelas recompor a superfície, o que aparecia como piscada em adaptadores de vídeo USB (DisplayLink), drivers antigos e sessões remotas.
- Corrigidos os rótulos cortados no painel de status ("Inicialização com Windows:", "Compatibilidade:") e nas legendas longas dos interruptores.
- Corrigido: abrir a seção pela primeira vez já marcava a personalização como ativa e registrava o Pulse na inicialização do Windows, sem o usuário ter aplicado nada. Agora nada acontece até aplicar um tema.

## 1.6.0 — 19/09/2026

- Nova seção **Personalização do Windows**: muda o visual da barra de tarefas, do Explorador de Arquivos, do Menu Iniciar e das Configurações, com temas completos, preview e reversão total.
  - **Aparências**: transparente, translúcido, blur, glass, acrylic, mica, cor sólida, gradiente e imagem de fundo, com ajustes de opacidade, transparência, intensidade de blur e de efeitos, cor/tint, saturação, luminosidade, escurecimento, raio de borda e posicionamento/escala/recorte da imagem.
  - **Explorer por regiões**: fundo principal, painel lateral, barra superior, barra de endereço, barra de comandos, pesquisa, abas e painel de detalhes têm estilos independentes — dá para misturar imagem no fundo com blur na lateral e glass nas abas.
  - **Temas**: criar, salvar, renomear, duplicar, excluir, aplicar, importar e exportar (.pulsetheme, com as imagens embutidas). Seis presets de fábrica: Windows Default, Pulse Glass, Transparent, Dark Glass, Fluent e Custom.
  - **Sincronizar aparência** aplica o estilo da barra de tarefas aos demais componentes; desligando, cada um volta a ser independente.
  - **Confirmação com reversão automática**: ao aplicar um tema, o Pulse pergunta se deve mantê-lo e desfaz sozinho em 5 segundos se ninguém responder — se algo ficar ilegível, não fazer nada é o caminho seguro.
  - **Persistência sem depender do app**: um host de inicialização reaplica o tema no logon e quando o Explorer reinicia, sem manter o Pulse aberto.
  - **Reversibilidade**: botões para restaurar cada componente e um para remover toda a personalização, mais um atalho de recuperação (`Restaurar-Windows.cmd`) que devolve o Windows ao padrão mesmo com o Pulse fechado ou quebrado. Nenhum arquivo do Windows é modificado: os efeitos vivem só na memória das janelas.
  - **Proteção contra falhas**: um alvo que falhe repetidamente entra em quarentena e é restaurado automaticamente, sem ciclos de crash e reaplicação.
  - **Compatibilidade honesta**: opções que a build atual do Windows não suporta aparecem desativadas com o motivo, e uma incompatibilidade em um componente não afeta os demais.
  - Menu Iniciar, Configurações e as áreas internas do Explorer dependem de um motor avançado (beta) que não acompanha esta versão; esses alvos aparecem marcados como indisponíveis, e a barra de tarefas e a janela do Explorer funcionam normalmente sem ele.

## 1.5.2 — 19/09/2026

- As seções do perfil de jogo que estão em "Não alterar" ou "Automático" passam a explicar por que os campos estão esmaecidos: "Mude o seletor acima para Personalizado para editar estes campos." Antes, campos desligados de propósito davam a impressão de estarem quebrados.
- Corrigidas as caixas de seleção brancas com o texto invisível na configuração de perfis de jogo. Todas as caixas apareciam como blocos cinza-claros (RGB 240,240,240) e a lista suspensa, branca. A causa: o estilo implícito de caixa de seleção do aplicativo herdava de um recurso resolvido enquanto o dicionário ainda estava sendo montado, e acabava caindo no tema claro padrão do Windows em vez do tema escuro. As janelas do GameHub agora apontam para o estilo escuro do Wpf.Ui por uma chave explícita, sem depender da ordem de carregamento.

## 1.5.1 — 18/09/2026

- Corrigidas as barras e faixas brancas na configuração de perfis dos jogos. A trilha dos deslizantes usava um branco a 55% de opacidade mesmo quando o controle estava ativo, o que deixava barras claras atravessando a janela escura. A trilha agora é cinza-escura e o preenchimento usa a cor de marca. O mesmo valia para as Configurações do aplicativo.

## 1.5.0 — 18/09/2026

- Corrigido o texto ilegível nas seções desativadas do editor de perfis. As seções em "Não alterar" ficam esmaecidas de propósito, mas o tom usado tinha contraste de cerca de 2:1 sobre o fundo escuro, o que fazia os campos parecerem vazios quando na verdade o conteúdo estava lá.
- Corrigida a travadinha do GameHub ao percorrer a biblioteca. As imagens de capa e de destaque eram decodificadas no mesmo fio da interface: parar num jogo custava cerca de 36 ms, o equivalente a dois quadros perdidos. Agora a decodificação acontece em segundo plano.

## 1.4.9 — 18/09/2026

- Corrigidos os campos brancos com texto invisível na página de Configurações e nas demais telas do aplicativo. A janela principal dependia do efeito Mica do Windows para ter um fundo escuro; quando o sistema não o aplicava (efeitos de transparência desligados, sessão remota, certos drivers de vídeo), ela caía em um fundo claro e as caixas de seleção e de texto, que são translúcidas, viravam blocos brancos.

## 1.4.8 — 18/09/2026

- A seção Sobre passa a exibir a versão realmente em execução: ela estava fixada em "1.0.0" desde o primeiro lançamento.
- Adicionado o histórico de versões à seção Sobre, disponível sem conexão com a internet.

## 1.4.7 — 18/09/2026

- Corrigidos os campos ilegíveis nas janelas do GameHub: as caixas de seleção e de texto do editor de perfis apareciam como blocos brancos com o texto invisível.
- As janelas de emulador e de edição de jogo passam a seguir a mesma aparência do restante do GameHub.
- Corrigida também a lista suspensa das caixas de seleção, que é exibida fora da janela e mantinha as cores claras.
- O instalador agora identifica corretamente o Pulse1x em execução mesmo quando executado como administrador, fechando e reabrindo o aplicativo para concluir a atualização.

## 1.4.6 — 18/09/2026

- O botão B do controle passa a levar o seletor para os botões de salvar ou sair antes de fechar uma tela de configuração, em vez de descartar os ajustes de imediato.

## 1.4.5 — 18/09/2026

- Corrigida a inversão do analógico causada pelo retorno da alavanca ao centro.
- Corrigido o anel de foco desalinhado no menu lateral.
- Corrigido o tema da janela de perfis, que exibia texto escuro sobre fundo claro.

## 1.4 — 18/09/2026

Introdução do **GameHub**, uma biblioteca unificada de jogos com perfis de sistema por jogo.

**Biblioteca e capas**

- Detecção automática de jogos instalados pela Steam, Epic Games, GOG, EA e Ubisoft Connect, lendo os catálogos locais dos próprios lançadores — sem login e sem internet no caso da Steam.
- Busca de capas pelo nome através do catálogo público da Steam, o que também ilustra itens da Epic, GOG, adicionados manualmente e ROMs. Sem resultado, usa o ícone do executável e, por fim, uma capa gerada com o nome do jogo.
- Remoção de duplicados entre lojas: um título registrado por duas delas aparece uma única vez.
- Adição manual de executáveis, atalhos, pastas e emuladores com suas ROMs.

**Perfis por jogo**

- Cada seção funciona como "não alterar / automático / personalizado": plano de energia e ajustes avançados, modo do fabricante em notebooks, prioridade e afinidade de processo, otimização de memória, latência, rede, áudio, vídeo e os aplicativos a fechar ou abrir junto com o jogo.
- Todos os valores são lidos antes de serem alterados e gravados em disco, de modo que uma queda do jogo, do Pulse1x ou do computador ainda restaure o estado anterior na próxima execução.
- Predefinições editáveis: Padrão, Competitivo, Máximo Desempenho, Balanceado, Silencioso e Economia.

**Interface e controle**

- Navegação completa por controle (XInput), com menu lateral, teclado virtual para a busca e animações de abertura.
- Métricas de uso: tempo de jogo, FPS médio e máximo, médias por dia e uma seção de estatísticas.
- Personalização visual do aplicativo: cores, transparência, desfoque, intensidade das animações e plano de fundo.

Também nesta série: correção da leitura de acentos na saída do `powercfg`, que criava planos de energia duplicados a cada execução.

## 1.1.1 — 12/07/2026

- Corrigida a abertura do aplicativo logo após a instalação, que falhava com erro de elevação 740.

## 1.1.0 — 12/07/2026

- Novo painel **Status dos Servidores** dentro de Latência, com cartões por empresa nas categorias Jogos, Comunicação, Streaming, IA, Infraestrutura e Armazenamento, busca e atualização automática.
- Situação obtida das APIs oficiais quando disponíveis, com verificação de acessibilidade como alternativa — antes a maioria das empresas aparecia como fora do ar.
- Seções mais densas da página de Latência agora podem ser recolhidas.
- Corrigido o aviso de "atualização disponível" que aparecia a cada abertura.
- Corrigida a demora na atualização do ícone na busca do Menu Iniciar após atualizar.

## 1.0.1 — 11/07/2026

- Atualização automática pelas versões publicadas no GitHub, com aviso na abertura quando há uma versão nova.
- Falhas na instalação da atualização passam a ser exibidas, com nova tentativa quando um antivírus bloqueia o arquivo temporariamente.
- Melhorias na tradução de várias partes do aplicativo.

## 1.0.0 — 28/06/2026

- Primeira versão do Pulse1x: monitor de hardware para Windows.
