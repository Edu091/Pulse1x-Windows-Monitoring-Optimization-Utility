# Histórico de versões

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
