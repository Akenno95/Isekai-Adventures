<!-- Example-Hediff -->
	<HediffDef Name="ComboTestHediff" Abstract="True">
		<label>Combo Test Hediff</label>
		<comps>
        <!-- ComboReactor -->
			<li Class="IsekaiAdventures.HediffCompProperties_ComboReactor">
				<hediffReactions>
					<li>
						<reactionHediff>	<!-- One hediff must already be present to fire -->
							<li>Undead</li>
							<li>Poisoned</li>
						</reactionHediff>
						<requireAll>true</requireAll>		<!-- Standard=False ; If true, all listed hediffs must be present to react -->
						<reactionProperties>	<!-- How it'll react once it fires -->
							<damageDef>Holy</damageDef>
							<damageRange>15~30</damageRange>
							<armourPenetrationRange>0.2~0.8</armourPenetrationRange>
							<stunTicks>200</stunTicks>
							<hediffToAdd>Burning</hediffToAdd>
							<isAOE>true</isAOE>		<!-- AOE with full damage, stuns and hediff to everyone inside the radius -->
							<radius>4</radius>
						</reactionProperties>
						<removeOnReact>true</removeOnReact>		<!-- Standard=False ; If true, the hediff will be removed after reacting -->
					</li>
				</hediffReactions>

				<damageReactions>
					<li>
						<reactionDamageType>	<!-- One damage type needs to hit the pawn, after this hediff will react -->
							<li>Fire</li>
							<li>Bullet</li>
						</reactionDamageType>
						<reactionProperties>
							<damageDef>Explosion</damageDef>
							<damageRange>10~25</damageRange>
							<armourPenetrationRange>0.5~1.0</armourPenetrationRange>
							<stunTicks>100</stunTicks>
							<hediffToAdd>Shocked</hediffToAdd>
							<isAOE>false</isAOE>
							<radius>1</radius>
						</reactionProperties>
						<removeOnReact>false</removeOnReact>
					</li>
					<li>						<!-- Multiple reactionTypes can be used for other outcomes of the same hediff, same for hediffReactions -->
						<reactionDamageType>
							<li>Frost</li>
						</reactionDamageType>
						<reactionProperties>
							<damageDef>Frostbite</damageDef>
							<damageRange>5~15</damageRange>
							<armourPenetrationRange>0.0~0.3</armourPenetrationRange>
							<stunTicks>50</stunTicks>
							<hediffToAdd>Frozen</hediffToAdd>
							<isAOE>true</isAOE>
							<radius>6</radius>
						</reactionProperties>
						<removeOnReact>true</removeOnReact>
					</li>
				</damageReactions>
			</li>

        <!-- Regeneration -->
            <li Class="IsekaiAdventures.HediffCompProperties_Regenerate">
				<tickInterval>60</tickInterval> <!-- every 60 ticks (1 sec) -->
				<!-- Verletzungen (nur NICHT-permanent) Basis-Budget -->
				<healFlat>0.5</healFlat>
				<healPercentOfMissing>0.02</healPercentOfMissing>
				<healPercentOfMax>0.005</healPercentOfMax>
				<flatHealBonusStat>IAS_FlatRegenBonus</flatHealBonusStat>

				<!-- Globalfaktor (wirkt auf ALLES) -->
				<totalHealFactorStat>IAS_TotalHealFactor</totalHealFactorStat>  <!-- Goes on everything -->

				<!-- Filter -->
				<naturalPartsOnly>true</naturalPartsOnly>
				<!-- optional: nur bestimmte BodyParts -->
				<!-- <includeParts>
					<li>Arm</li>
					<li>Leg</li>
				</includeParts> -->
				
				<!-- optional: nur Parts mit bestimmten Tags (BodyPartTagDef.defName) -->
				<!-- <includePartTags>
					<li>MovingLimbCore</li>
				</includePartTags> -->
				
				<!-- Scars (permanent) separat, NICHT aus totalHeal -->
				<canHealScars>true</canHealScars>
				<scarSeverityReducePerTick>0.06</scarSeverityReducePerTick>

				<!-- Limb-Regeneration (separat, NICHT aus totalHeal) -->
				<canRegenLimbs>true</canRegenLimbs>
				<ticksPerLimb>60000</ticksPerLimb>     <!-- 60k = 1 Day -->
				<regrowthIndicatorHediff>IAS_RegrowthIndicator</regrowthIndicatorHediff>
				<removeIndicatorOnComplete>true</removeIndicatorOnComplete>

				<!-- Non-Injury-Reduktion separat (z. B. ToxicBuildup) -->
				<healNonInjuryHediffs>true</healNonInjuryHediffs>
				<hediffsToReduce>
					<li>ToxicBuildup</li>
				</hediffsToReduce>
				<hediffSeverityReducePerTick>0.005</hediffSeverityReducePerTick>
				
				<!-- Tend-Logik: Tend verbraucht Budget, kommt VOR Heilung -->
				<tendBleeding>true</tendBleeding>              <!-- blutende Injuries zuerst tenden -->
				<allowPartialTend>true</allowPartialTend>      <!-- wenn Budget knapp: niedrigere Qualität -->
				<tendTargetQuality>0.6</tendTargetQuality>     <!-- gewünschte Qualität 0..1 -->
				<tendCostPerQuality>1.0</tendCostPerQuality>   <!-- Kosten (Heal-Budget) pro 1.0 Qualität -->
				<minTendQuality>0.1</minTendQuality>           <!-- Mindestqualität bei Partial-Tend -->

				<!-- Fresh MissingParts können einen separaten Tend-Pool bekommen -->
				<tendFreshMissingParts>true</tendFreshMissingParts>
				<tendGlobalPoolPerTick>0.5</tendGlobalPoolPerTick> <!-- z.B. 0.8 = pro Tick 0.8 „Budget“ nur fürs Tend von MissingParts -->
			</li>
		</comps>
	</HediffDef>


