export type Mode = "guide" | "writer" | "roleplay" | "lore" | "forge" | "council" | "research" | "games";

export interface ReasoningArtifact {
  agentId: string;
  agentLabel: string;
  content: string;
  sequence: number;
}

export interface MessageVariant {
  content: string;
  responseType?: string;
  timestamp: number;
  portrait?: string | null;
  reasoning?: string | null;
  reasoningArtifacts?: ReasoningArtifact[];
}

export interface Message {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  responseType?: string;
  timestamp: number;
  /** Portrait URL for assistant messages (roleplay mode). */
  portrait?: string | null;
  /** Portrait URL for user messages (roleplay mode). */
  userPortrait?: string | null;
  /** Reasoning/thinking content from the model (e.g. DeepSeek reasoning). */
  reasoning?: string | null;
  reasoningArtifacts?: ReasoningArtifact[];
  /** Alternative responses (for swipe). Index 0 is the original. */
  variants?: MessageVariant[];
  /** Currently displayed variant index (0-based). */
  activeVariant?: number;
  /** Backend parent node ID — used for regeneration via parentId. */
  parentId?: string | null;
}

export interface Status {
  status: string;
  version: string;
  build: string;
  mode: Mode;
  profile: string;
  project: string | null;
  file: string | null;
  loreFiles: number;
  loreSet: string;
  writingStyle: string;
  model: string;
  conversationTurns: number;
  layout: string;
  contextLimit: number;
  loreTokens: number;
  historyTokens: number;
  diagnosticsLivePanel?: boolean;
  aiCharacter: string;
  userCharacter: string;
  update?: { available: boolean; version: string | null; url: string | null } | null;
}

export interface DiagnosticEntry {
  category: string;
  message: string;
  level: "info" | "warning" | "error";
}

export interface Profiles {
  profileIds: string[];
  defaultProfileId: string;
  activeProfileId: string;
  loreSets: string[];
  narrativeRules: string[];
  writingStyles: string[];
  librarianPrompts: string[];
  activeLore: string;
  activeNarrativeRules: string;
  activeWritingStyle: string;
  activeLibrarianPrompt: string;
}

export interface ModeInfo {
  sessionId?: string | null;
  mode: Mode;
  project: string | null;
  file: string | null;
  character: string | null;
  pendingContent: string | null;
  pendingProject: string | null;
  pendingFile: string | null;
  notice: string | null;
}

export interface ProfileSwitchResult {
  sessionId?: string | null;
  activeProfileId: string;
  activeLore: string;
  activeNarrativeRules: string;
  activeWritingStyle: string;
  activeLibrarianPrompt: string;
  loreFiles: number;
  status?: string;
}

export interface AgentUsage {
  agent: string;
  input: number;
  output: number;
  requests: number;
}

export interface SessionUsage {
  totalInput: number;
  totalOutput: number;
  totalRequests: number;
  byAgent: AgentUsage[];
}

export interface ProjectEntry {
  name: string;
  files: string[];
}

export interface ProjectList {
  mode: string;
  directory: string;
  projects: ProjectEntry[];
}

export type GameRuntimeStatus = "NotStarted" | "Running" | "WaitingForInput" | "Resolving" | "WaitingOnAgentTurns" | "Ended" | "Aborted";
export type GameParticipantKind = "Human" | "Agent" | "System";
export type ParticipantFeedEntryKind = "PublicChannelMessage" | "DirectMessage" | "GameEventLink" | 0 | 1 | 2;
export type PendingInputStatus = "Waiting" | "Submitted" | "TimedOut" | "Cancelled" | 0 | 1 | 2 | 3;

export interface GameIdentifier {
  value: string;
}

export interface GameViewResponse {
  view: GameBridgeView;
}

export interface GameMutationResponse {
  view: GameBridgeView;
  runtimeEventTypes: string[];
  engineEventTypes: string[];
  communicationEventTypes: string[];
}

export interface GameBridgeView {
  status: GameRuntimeStatus;
  gameInstanceId: string | null;
  templateId: string | null;
  moduleId: string | null;
  moduleVersion: string | null;
  roundNumber: number | null;
  stageId: string | null;
  stageName: string | null;
  roster: GameBridgeParticipantView[];
  public: GameBridgePublicView;
  player: GameBridgePlayerView | null;
  moduleAuthoring: GameBridgeModuleAuthoringView | null;
}

export interface GameBridgeModuleAuthoringView {
  setupFields: GameBridgeSetupFieldView[];
  stages: GameBridgeStageHookView[];
  actionForms: GameBridgeActionFormView[];
  promptAssets: GameBridgePromptAssetView[];
  communicationCapabilities: GameBridgeCommunicationCapabilitiesView;
  memoryExpectations: GameBridgeMemoryExpectationsView;
  projectionCapabilities: GameBridgeProjectionCapabilitiesView;
}

export interface GameBridgeSetupFieldView {
  name: string;
  valueKind: GameSetupValueKind;
  isRequired: boolean;
  displayName: string;
  description: string;
}

export interface GameBridgeStageHookView {
  stageId: string;
  displayName: string;
  description: string;
  sequence: number;
  allowsPublicMessages: boolean;
  allowsDirectMessages: boolean;
}

export interface GameBridgeActionFormView {
  intentName: string;
  stageId: string;
  displayName: string;
  description: string;
  layout: string;
  fields: GameBridgeActionFieldView[];
}

export interface GameBridgeActionFieldView {
  name: string;
  valueKind: string;
  isRequired: boolean;
  displayName: string;
  description: string;
}

export interface GameBridgePromptAssetView {
  assetId: string;
  kind: string;
  isRequired: boolean;
}

export interface GameBridgeCommunicationCapabilitiesView {
  allowsPublicChannelMessages: boolean;
  allowsDirectMessages: boolean;
}

export interface GameBridgeMemoryExpectationsView {
  usesRoundSummaries: boolean;
  suggestedSummaryTokenBudget: number;
  maximumRetainedRoundSummaries: number;
}

export interface GameBridgeProjectionCapabilitiesView {
  supportsPublicEventProjection: boolean;
  supportsParticipantPrivateProjection: boolean;
  supportsHostInspectorProjection: boolean;
}

export interface GameBridgePublicView {
  narration: GameBridgeNarrationEntry[];
  feed: ParticipantFeedEntry[];
}

export interface GameBridgeParticipantView {
  participantId: string;
  displayName: string;
  kind: GameParticipantKind;
  isJoined: boolean;
  isCurrentPlayer: boolean;
}

export interface GameBridgePlayerView {
  participantId: string;
  displayName: string;
  engineEvents: VisibleGameEvent[];
  pendingInputs: PendingInputState[];
  feed: ParticipantFeedEntry[];
  cursor: GameRuntimeEventDeliveryCursor | null;
  actionForms: GameBridgeActionFormView[];
}

export interface GameBridgeNarrationEntry {
  eventId: string;
  sequence: number;
  eventType: string;
  text: string;
  occurredAt: string;
}

export interface VisibleGameEvent {
  eventId: GameIdentifier;
  sequence: number;
  eventType: string;
  occurredAt: string;
}

export interface PendingInputState {
  pendingInputId: GameIdentifier;
  participantId: GameIdentifier;
  stageId: GameIdentifier;
  intentName: string;
  status: PendingInputStatus;
  legalOptions: LegalIntentOption[];
}

export interface LegalIntentOption {
  intentName: string;
  displayName: string;
  description: string;
}

export interface ParticipantFeedEntry {
  sequence: number;
  kind: ParticipantFeedEntryKind;
  messageId: string | null;
  linkId: string | null;
  author: ParticipantMessageAuthor | null;
  recipientParticipantIds: GameIdentifier[];
  text: string | null;
  gameEventId: string | null;
  gameEventSequence: number | null;
  summary: string | null;
  createdAt: string;
}

export interface ParticipantMessageAuthor {
  participantId: GameIdentifier;
  kind: "Human" | "Agent" | "System" | 0 | 1 | 2;
}

export interface GameRuntimeEventDeliveryCursor {
  participantId: string;
  deliveredThroughEngineEventSequence: number;
  deliveredThroughCommunicationSequence: number;
  memoryRevision: number;
  lastPromptEnvelopeId: string | null;
}

export type GameTemplateRuleOptionValueKind = "String" | "Int" | "Bool" | "ParticipantId" | "ParticipantSet";
export type GameTemplateRandomNameBehavior = "UseFixedNameWhenProvided" | "AlwaysRandomize" | "NeverRandomize";
export type GameSetupValueKind = "String" | "Int" | "Bool" | "ParticipantId" | "ParticipantSet";

export interface GameTemplateSummary {
  templateId: string;
  displayName: string;
  moduleId: string;
  minimumModuleVersion: string;
  maximumModuleVersion: string;
}

export interface GameTemplateListResponse {
  templates: GameTemplateSummary[];
}

export interface GameTemplateCatalogResponse {
  modules: GameTemplateModuleOption[];
  providers: GameTemplateProviderOption[];
}

export interface GameTemplateModuleOption {
  moduleId: string;
  moduleVersion: string;
  displayName: string;
  minimumTemplateVersion: string;
  maximumTemplateVersion: string;
  minimumPlayers: number;
  maximumPlayers: number;
  setupFields: GameTemplateSetupFieldOption[];
  stages: GameTemplateStageHookOption[];
  actionForms: GameTemplateActionFormOption[];
  promptAssets: GameTemplatePromptAssetOption[];
  communicationCapabilities: GameTemplateCommunicationCapabilitiesOption;
  memoryExpectations: GameTemplateMemoryExpectationsOption;
  participantRequirements: GameTemplateParticipantRequirementsOption;
  projectionCapabilities: GameTemplateProjectionCapabilitiesOption;
}

export interface GameTemplateSetupFieldOption {
  name: string;
  valueKind: GameSetupValueKind;
  isRequired: boolean;
  displayName: string;
  description: string;
}

export interface GameTemplateStageHookOption {
  stageId: string;
  displayName: string;
  description: string;
  sequence: number;
  allowsPublicMessages: boolean;
  allowsDirectMessages: boolean;
}

export interface GameTemplateActionFormOption {
  intentName: string;
  stageId: string;
  displayName: string;
  description: string;
  layout: string;
  fields: GameTemplateActionFieldOption[];
}

export interface GameTemplateActionFieldOption {
  name: string;
  valueKind: string;
  isRequired: boolean;
  displayName: string;
  description: string;
}

export interface GameTemplatePromptAssetOption {
  assetId: string;
  kind: string;
  isRequired: boolean;
}

export interface GameTemplateCommunicationCapabilitiesOption {
  allowsPublicChannelMessages: boolean;
  allowsDirectMessages: boolean;
}

export interface GameTemplateMemoryExpectationsOption {
  usesRoundSummaries: boolean;
  suggestedSummaryTokenBudget: number;
  maximumRetainedRoundSummaries: number;
}

export interface GameTemplateParticipantRequirementsOption {
  allowsHumanParticipants: boolean;
  allowsAgentParticipants: boolean;
  allowsSystemParticipants: boolean;
  minimumHumanParticipants: number;
  minimumAgentParticipants: number;
}

export interface GameTemplateProjectionCapabilitiesOption {
  supportsPublicEventProjection: boolean;
  supportsParticipantPrivateProjection: boolean;
  supportsHostInspectorProjection: boolean;
}

export interface GameTemplateProviderOption {
  alias: string;
  type: string;
  model: string | null;
  defaultModel: string | null;
  contextLimit: number | null;
}

export interface GameTemplateResponse {
  template: GameTemplate;
  validation: GameTemplateValidationResult;
}

export interface ValidateGameTemplateResponse {
  validation: GameTemplateValidationResult;
}

export interface DeleteGameTemplateResponse {
  status: string;
  templateId: string;
}

export interface GameTemplate {
  templateId: string;
  displayName: string;
  description: string | null;
  module: GameTemplateModuleSelection;
  templateVersion: string;
  rulesOptions: GameTemplateRulesOptions;
  roster: GameTemplateRosterSettings;
  memory: GameTemplateMemorySettings;
  communication: GameTemplateCommunicationSettings;
  naming: GameTemplateNamingSettings;
}

export interface GameTemplateModuleSelection {
  moduleId: string;
  minimumVersion: string;
  maximumVersion: string;
}

export interface GameTemplateRulesOptions {
  values: GameTemplateRuleOptionValue[];
}

export interface GameTemplateRuleOptionValue {
  name: string;
  kind: GameTemplateRuleOptionValueKind;
  stringValue: string | null;
  intValue: number | null;
  boolValue: boolean | null;
  participantIdValue: string | null;
  participantSetValue: string[];
}

export interface GameTemplateRosterSettings {
  rosterSize: number;
  userSeatParticipantId: string | null;
  agentPlayers: GameTemplateAgentPlayerConfig[];
}

export interface GameTemplateAgentPlayerConfig {
  participantId: string;
  providerAlias: string;
  modelOverride: string | null;
  characterPrompt: string | null;
  personality: string | null;
  fixedName: string | null;
  randomNameBehavior: GameTemplateRandomNameBehavior;
}

export interface GameTemplateMemorySettings {
  tokenBudget: number;
}

export interface GameTemplateCommunicationSettings {
  publicChannelEnabled: boolean;
  directMessagesEnabled: boolean;
  hostMessagesEnabled: boolean;
}

export interface GameTemplateNamingSettings {
  randomizeAgentNames: boolean;
  randomNameSet: string | null;
  randomSeed: number | null;
}

export interface GameTemplateValidationResult {
  issues: GameTemplateValidationIssue[];
  isValid: boolean;
}

export interface GameTemplateValidationIssue {
  code: string;
  message: string;
  field: string | null;
  source: string;
}


export interface LoreCanonizationProposal {
  sessionId: string;
  loreSet: string;
  targetFilePath: string;
  summary: string;
  newFacts: string[];
  modifiedFacts: string[];
  conflicts: string[];
  proposedMarkdown: string;
  proposedFileContent: string;
  canApply: boolean;
  generatedAt: string;
}

export interface LoreCanonizationPreviewResult {
  sessionId: string;
  status: string;
  proposal: LoreCanonizationProposal;
}

export interface LoreCanonizationApplyResult {
  sessionId: string;
  status: string;
  loreSet: string;
  targetFilePath: string;
  contentLength: number;
}

export interface LoreCanonizationDiscardResult {
  sessionId: string;
  status: string;
  targetFilePath: string | null;
}
