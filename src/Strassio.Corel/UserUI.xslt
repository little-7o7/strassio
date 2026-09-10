<?xml version="1.0"?>
<!--
  Добавляет пункт «Strassio» в меню «Окно → Докеры» (SPEC.md, раздел 2.1: «Дубль в меню
  Окно → Докеры → Strassio»). Формат и GUID меню «Окно» (cdb9ea9c-...) и якоря внутри него
  (47cc9a2d-...) взяты как есть из рабочего примера bonus630 (DockerTemplateX7/DockerTemplateCS,
  X7+) — см. CLAUDE.md, раздел "Совместимость с CorelDRAW". Требует проверки автором.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:frmwrk="Corel Framework Data"
                exclude-result-prefixes="frmwrk">
	<xsl:output method="xml" encoding="UTF-8" indent="yes"/>

	<frmwrk:uiconfig>
		<frmwrk:compositeNode xPath="/uiConfig/commandBars/commandBarData[@guid='cdb9ea9c-223e-4865-97a6-31c007c69674']"/>
		<frmwrk:compositeNode xPath="/uiConfig/commandBars/commandBarData[@guid='8011a906-9446-409f-ab38-56696a98f82d']"/>
		<frmwrk:compositeNode xPath="/uiConfig/frame"/>
	</frmwrk:uiconfig>

	<!-- Копируем всё как есть -->
	<xsl:template match="node()|@*">
		<xsl:copy>
			<xsl:apply-templates select="node()|@*"/>
		</xsl:copy>
	</xsl:template>

	<!-- Вспомогательный шаблон: вставить новый пункт в меню/панель -->
	<xsl:template match="node()|@*" mode="insert-item">
		<xsl:param name="after"></xsl:param>
		<xsl:param name="before"></xsl:param>
		<xsl:param name="content"></xsl:param>
		<xsl:copy>
			<xsl:apply-templates select="@*"/>
			<xsl:for-each select="node()">
				<xsl:if test="name()='item' and @guidRef=$before">
					<xsl:copy-of select="$content"/>
				</xsl:if>
				<xsl:copy>
					<xsl:apply-templates select="node()|@*"/>
				</xsl:copy>
				<xsl:if test="name()='item' and @guidRef=$after">
					<xsl:copy-of select="$content"/>
				</xsl:if>
			</xsl:for-each>
			<xsl:if test="not(./item[@guidRef=$after]) and not(./item[@guidRef=$before])">
				<xsl:copy-of select="$content"/>
			</xsl:if>
		</xsl:copy>
	</xsl:template>

	<!-- Добавляем «Strassio» в конец меню «Окно → Докеры» -->
	<xsl:template match="commandBarData[@guid='cdb9ea9c-223e-4865-97a6-31c007c69674']/menu">
		<xsl:apply-templates mode="insert-item" select=".">
			<xsl:with-param name="content">
				<xsl:if test="not(./item[@guidRef='aabc2eee-195c-4ae1-9258-2d8aa0de3eb7'])">
					<item guidRef="aabc2eee-195c-4ae1-9258-2d8aa0de3eb7"/>
				</xsl:if>
			</xsl:with-param>
			<xsl:with-param name="after" select="'47cc9a2d-0b7a-4df1-a686-ea3aa21b4631'"/>
		</xsl:apply-templates>
	</xsl:template>

</xsl:stylesheet>
