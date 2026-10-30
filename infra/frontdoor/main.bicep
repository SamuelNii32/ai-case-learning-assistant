targetScope = 'resourceGroup'

@description('Globally unique Azure Front Door profile name.')
param profileName string

@description('Globally unique Front Door endpoint name. Its generated hostname can be used for staging before DNS approval.')
param endpointName string

@description('Vercel deployment hostname only, without scheme or path.')
param frontendOriginHost string

@description('Azure App Service API hostname only, without scheme or path.')
param apiOriginHost string

@allowed([
  'Standard_AzureFrontDoor'
  'Premium_AzureFrontDoor'
])
param skuName string = 'Standard_AzureFrontDoor'

@minValue(16)
@maxValue(240)
param originResponseTimeoutSeconds int = 240

resource profile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: profileName
  location: 'global'
  sku: {
    name: skuName
  }
  properties: {
    originResponseTimeoutSeconds: originResponseTimeoutSeconds
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: profile
  name: endpointName
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource apiGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'casepilot-api'
  properties: {
    sessionAffinityState: 'Disabled'
    healthProbeSettings: {
      probePath: '/health/live'
      probeRequestType: 'HEAD'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 60
    }
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
  }
}

resource apiOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: apiGroup
  name: 'azure-app-service'
  properties: {
    hostName: apiOriginHost
    originHostHeader: apiOriginHost
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}

resource frontendGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'casepilot-frontend'
  properties: {
    sessionAffinityState: 'Disabled'
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
  }
}

resource frontendOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: frontendGroup
  name: 'vercel'
  properties: {
    hostName: frontendOriginHost
    originHostHeader: frontendOriginHost
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}

resource apiRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'api-and-bff'
  dependsOn: [apiOrigin]
  properties: {
    originGroup: { id: apiGroup.id }
    supportedProtocols: ['Http', 'Https']
    patternsToMatch: [
      '/bff/*'
      '/signin-auth0'
      '/signout-callback-auth0'
      '/me'
      '/auth/*'
      '/uploads/*'
      '/sessions/*'
      '/classes/*'
      '/index/*'
      '/ask/*'
      '/search/*'
      '/tutor/*'
      '/cases'
      '/admin/*'
      '/api/*'
      '/health/*'
      '/healthz'
      '/ping'
    ]
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
    httpsRedirect: 'Enabled'
    enabledState: 'Enabled'
  }
}

resource frontendRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'frontend'
  dependsOn: [frontendOrigin]
  properties: {
    originGroup: { id: frontendGroup.id }
    supportedProtocols: ['Http', 'Https']
    patternsToMatch: ['/*']
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
    httpsRedirect: 'Enabled'
    enabledState: 'Enabled'
  }
}

output frontDoorHostName string = endpoint.properties.hostName
output frontDoorProfileId string = profile.id
