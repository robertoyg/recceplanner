targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

@secure()
param anthropicApiKey string

// ── Variables ─────────────────────────────────────────────────────────────────

var location = 'westus2'

// ── Azure Container Registry ──────────────────────────────────────────────────

resource recceplanneracr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'recceplanneracr'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// ── Log Analytics Workspace ───────────────────────────────────────────────────

resource recceplannerlogs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'recceplanner-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Container Apps Environment ────────────────────────────────────────────────

resource recceplannerenv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'recceplanner-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: recceplannerlogs.properties.customerId
        sharedKey: recceplannerlogs.listKeys().primarySharedKey
      }
    }
  }
}

// ── MCP Server Container App ──────────────────────────────────────────────────

resource recceplannermcp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'recceplanner-mcp'
  location: location
  dependsOn: [
    recceplanneracr
    recceplannerenv
  ]
  properties: {
    environmentId: recceplannerenv.id
    configuration: {
      // ACR registry credentials added by GitHub Actions on first image push
      ingress: {
        external: false
        targetPort: 80  // helloworld listens on 80; CI will update to 5000
        transport: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'recceplanner-mcp'
          // Placeholder — GitHub Actions replaces this on first push to main
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// ── Agent API Container App ───────────────────────────────────────────────────

resource recceplanneragent 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'recceplanner-agent'
  location: location
  dependsOn: [
    recceplanneracr
    recceplannerenv
    recceplannermcp
  ]
  properties: {
    environmentId: recceplannerenv.id
    configuration: {
      // ACR registry credentials and anthropic-key secret added by GitHub Actions on first deploy
      secrets: [
        {
          name: 'anthropic-key'
          value: anthropicApiKey
        }
      ]
      ingress: {
        external: true
        targetPort: 80  // helloworld listens on 80; CI will update to 8000
        transport: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'recceplanner-agent'
          // Placeholder — GitHub Actions replaces this on first push to main
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            {
              name: 'MCP_SERVER_URL'
              value: 'http://recceplanner-mcp'
            }
            {
              name: 'CLAUDE_MODEL'
              value: 'claude-sonnet-4-6'
            }
            {
              // Set to * initially; CI updates this to the SWA hostname after first deploy
              name: 'CORS_ORIGINS'
              value: '*'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
      }
    }
  }
}

// ── Static Web App ────────────────────────────────────────────────────────────
// Deployed separately via GitHub Actions; Bicep provisions the resource only.

resource recceplannerWeb 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'recceplanner-web'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('ACR login server (used by CI to push images)')
output acrLoginServer string = recceplanneracr.properties.loginServer

@description('Agent API ingress FQDN (external URL for the FastAPI service)')
output agentApiUrl string = recceplanneragent.properties.configuration.ingress.fqdn

@description('Static Web App default hostname')
output staticWebAppDefaultHostname string = recceplannerWeb.properties.defaultHostname

// staticWebAppDeploymentToken is intentionally NOT an output — Bicep outputs are
// stored in deployment history in plain text. Retrieve the token manually after deploy:
//
//   az staticwebapp secrets list \
//     --name recceplanner-web \
//     --resource-group ReccePlanner \
//     --query "properties.apiKey" -o tsv
