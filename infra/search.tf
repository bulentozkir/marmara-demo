resource "azurerm_search_service" "app" {
  name                          = "srch-${local.resource_prefix}"
  resource_group_name           = data.azurerm_resource_group.target.name
  location                      = local.location
  sku                           = "free"
  local_authentication_enabled  = false
  public_network_access_enabled = true
  tags                          = local.tags
}

resource "azurerm_role_assignment" "search_documents" {
  scope                            = azurerm_search_service.app.id
  role_definition_name             = "Search Index Data Contributor"
  principal_id                     = azurerm_user_assigned_identity.app.principal_id
  principal_type                   = "ServicePrincipal"
  skip_service_principal_aad_check = true
}

resource "azurerm_role_assignment" "search_schema_deployer" {
  scope                = azurerm_search_service.app.id
  role_definition_name = "Search Service Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "search_data_deployer" {
  scope                = azurerm_search_service.app.id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}
