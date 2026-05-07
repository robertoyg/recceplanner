using './main.bicep'

// ── Subscription / deployment target ─────────────────────────────────────────
// Subscription ID : 9b95613f-a4d0-4cab-93ac-5004db7186d2
// Resource Group  : ReccePlanner  (must exist before deploying)
// Location        : westus2
//
// Deploy with:
//   az deployment group create \
//     --subscription 9b95613f-a4d0-4cab-93ac-5004db7186d2 \
//     --resource-group ReccePlanner \
//     --template-file infra/main.bicep \
//     --parameters infra/main.bicepparam

// ── Parameters ────────────────────────────────────────────────────────────────

// REQUIRED: Set this to the Anthropic API key before deploying.
// Do NOT commit a real key here. Use one of:
//   - az deployment group create ... --parameters anthropicApiKey=$ANTHROPIC_API_KEY
//   - Replace the empty string below (local only, never commit)
param anthropicApiKey = ''
