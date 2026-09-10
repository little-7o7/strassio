<?xml version="1.0"?>
<!--
  Формат сверен с рабочими открытыми аддонами bonus630 для CorelDRAW X7+
  (DockerTemplateX7/DockerTemplateCS, Bonus630DevToolsBar, QrCodeDocker) — см. CLAUDE.md,
  раздел "Совместимость с CorelDRAW". Не проверено на реальном CorelDRAW — просьба автору
  проверить и прислать скриншот/текст ошибки после сборки.

  GUID'ы своих элементов (сгенерированы для Strassio, менять не нужно):
    aabc2eee-195c-4ae1-9258-2d8aa0de3eb7 — кнопка-камень (checkButton)
    de8a1f59-b170-430a-b64b-df1d9f00e24c — wpfhost (наш докер как control)
    7f7cf08a-ee52-4e41-9f8e-dc5fd8765581 — dockerData (сам докер)
    8011a906-9446-409f-ab38-56696a98f82d — commandBarData панели инструментов «Strassio»

  GUID'ы из примеров bonus630 (чужие, стандартные для CorelDRAW UI — используются как есть):
    2cc24a3e-fe24-4708-9a74-9c75406eebcd — dynamicCategory (общая категория команд)
    894bf987-2ec1-8f83-41d8-68f6797d0db4 / c2b44f69-6dec-444e-a37e-5dbf7ff43dae —
      «якорь»: dockHost и toolbar стандартной панели, под которую подставляется наша панель
-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:frmwrk="Corel Framework Data">
	<xsl:output method="xml" encoding="UTF-8" indent="yes"/>

	<frmwrk:uiconfig>
		<frmwrk:applicationInfo userConfiguration="true" />
	</frmwrk:uiconfig>

	<!-- Копируем всё как есть -->
	<xsl:template match="node()|@*">
		<xsl:copy>
			<xsl:apply-templates select="node()|@*"/>
		</xsl:copy>
	</xsl:template>

	<xsl:template match="uiConfig/items">
		<xsl:copy>
			<xsl:apply-templates select="node()|@*"/>

			<!-- Кнопка-камень: открывает и закрывает докер Strassio -->
			<itemData guid="aabc2eee-195c-4ae1-9258-2d8aa0de3eb7" noBmpOnMenu="true"
					  type="checkButton"
					  check="*Docker('7f7cf08a-ee52-4e41-9f8e-dc5fd8765581')"
					  dynamicCategory="2cc24a3e-fe24-4708-9a74-9c75406eebcd"
					  userCaption="Strassio"
					  enable="true"/>

			<!-- WPF-контрол, который CorelDRAW создаёт для нашего докера -->
			<itemData guid="de8a1f59-b170-430a-b64b-df1d9f00e24c"
					  type="wpfhost"
					  hostedType="Addons\Strassio\Strassio.Corel.dll,Strassio.Corel.Docker"
					  enable="true"/>

		</xsl:copy>
	</xsl:template>

	<!-- Своя панель инструментов «Strassio» с одной кнопкой (SPEC.md, раздел 2.1) -->
	<xsl:template match="uiConfig/commandBars">
		<xsl:copy>
			<xsl:apply-templates select="node()|@*"/>

			<commandBarData guid="8011a906-9446-409f-ab38-56696a98f82d"
							nonLocalizableName="Strassio"
							userCaption="Strassio"
							locked="true"
							type="toolbar">
				<toolbar>
					<item guidRef="aabc2eee-195c-4ae1-9258-2d8aa0de3eb7" dock="top"/>
				</toolbar>
			</commandBarData>
		</xsl:copy>
	</xsl:template>

	<!-- Пристыковываем панель Strassio под стандартными панелями инструментов -->
	<xsl:template match="uiConfig/containers/container[@guid='bee85f91-3ad9-dc8d-48b5-d2a87c8b2109']/container[@guid='Framework_MainFrame-layout']/dockHost[@guid='894bf987-2ec1-8f83-41d8-68f6797d0db4']/toolbar[@guidRef='c2b44f69-6dec-444e-a37e-5dbf7ff43dae']">
		<xsl:copy-of select="."/>
		<toolbar guidRef="8011a906-9446-409f-ab38-56696a98f82d" dock="top" />
	</xsl:template>

	<!-- Сам докер -->
	<xsl:template match="uiConfig/dockers">
		<xsl:copy>
			<xsl:apply-templates select="node()|@*"/>

			<dockerData guid="7f7cf08a-ee52-4e41-9f8e-dc5fd8765581"
						userCaption="Strassio"
						wantReturn="true"
						focusStyle="noThrow">
				<container>
					<item dock="fill" margin="0,0,0,0" guidRef="de8a1f59-b170-430a-b64b-df1d9f00e24c" />
				</container>
			</dockerData>
		</xsl:copy>
	</xsl:template>

</xsl:stylesheet>
