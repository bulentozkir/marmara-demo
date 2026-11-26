data "azurerm_client_config" "current" {}

data "azurerm_resource_group" "target" {
  name = var.resource_group_name
}

locals {
  location        = coalesce(var.location, data.azurerm_resource_group.target.location)
  suffix          = substr(sha256("${lower(data.azurerm_resource_group.target.id)}/${var.name_prefix}"), 0, 8)
  resource_prefix = "${var.name_prefix}-${local.suffix}"
  tags = {
    project       = var.name_prefix
    environment   = "demo"
    "managed-by"  = "terraform"
    "deployed-by" = data.azurerm_client_config.current.object_id
  }
}

resource "azurerm_virtual_network" "app" {
  name                = "vnet-${local.resource_prefix}"
  resource_group_name = data.azurerm_resource_group.target.name
  location            = local.location
  address_space       = ["10.42.0.0/16"]
  tags                = local.tags
}

resource "azurerm_subnet" "app" {
  name                 = "app-integration"
  resource_group_name  = data.azurerm_resource_group.target.name
  virtual_network_name = azurerm_virtual_network.app.name
  address_prefixes     = ["10.42.1.0/24"]

  delegation {
    name = "app-service"

    service_delegation {
      name    = "Microsoft.Web/serverFarms"
      actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
    }
  }
}

resource "azurerm_subnet" "private_endpoints" {
  name                              = "private-endpoints"
  resource_group_name               = data.azurerm_resource_group.target.name
  virtual_network_name              = azurerm_virtual_network.app.name
  address_prefixes                  = ["10.42.2.0/24"]
  private_endpoint_network_policies = "Disabled"
}

resource "azurerm_private_dns_zone" "sql" {
  name                = "privatelink.database.windows.net"
  resource_group_name = data.azurerm_resource_group.target.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone" "key_vault" {
  name                = "privatelink.vaultcore.azure.net"
  resource_group_name = data.azurerm_resource_group.target.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "sql" {
  name                 = "sql-${local.resource_prefix}"
  private_dns_zone_id  = azurerm_private_dns_zone.sql.id
  virtual_network_id   = azurerm_virtual_network.app.id
  registration_enabled = false
  tags                 = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "key_vault" {
  name                 = "kv-${local.resource_prefix}"
  private_dns_zone_id  = azurerm_private_dns_zone.key_vault.id
  virtual_network_id   = azurerm_virtual_network.app.id
  registration_enabled = false
  tags                 = local.tags
}

resource "azurerm_user_assigned_identity" "app" {
  name                = "id-${local.resource_prefix}"
  resource_group_name = data.azurerm_resource_group.target.name
  location            = local.location
  tags                = local.tags
}

resource "azurerm_mssql_server" "app" {
  name                          = "sql-${local.resource_prefix}-${coalesce(var.sql_location, local.location)}"
  resource_group_name           = data.azurerm_resource_group.target.name
  location                      = coalesce(var.sql_location, local.location)
  version                       = "12.0"
  minimum_tls_version           = "1.2"
  public_network_access_enabled = false
  connection_policy             = "Proxy"
  tags                          = local.tags

  azuread_administrator {
    login_username              = var.sql_administrator_login
    object_id                   = coalesce(var.sql_administrator_object_id, data.azurerm_client_config.current.object_id)
    tenant_id                   = data.azurerm_client_config.current.tenant_id
    azuread_authentication_only = true
  }
}

resource "azurerm_mssql_database" "app" {
  name                        = "todos"
  server_id                   = azurerm_mssql_server.app.id
  sku_name                    = var.sql_sku_name
  min_capacity                = startswith(var.sql_sku_name, "GP_S_") ? var.sql_min_capacity : null
  auto_pause_delay_in_minutes = startswith(var.sql_sku_name, "GP_S_") ? var.sql_auto_pause_delay_in_minutes : null
  max_size_gb                 = 2
  storage_account_type        = "Local"
  tags                        = local.tags
}

resource "azurerm_key_vault" "app" {
  name                          = "kv-${local.resource_prefix}"
  resource_group_name           = data.azurerm_resource_group.target.name
  location                      = local.location
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  public_network_access_enabled = false
  purge_protection_enabled      = true
  soft_delete_retention_days    = 7
  tags                          = local.tags

  network_acls {
    bypass         = "None"
    default_action = "Deny"
  }
}

# The management-plane API can create this password-free secret while the vault stays private.
resource "azapi_resource" "sql_connection" {
  type      = "Microsoft.KeyVault/vaults/secrets@2026-05-15"
  name      = "sql-connection"
  parent_id = azurerm_key_vault.app.id
  tags      = local.tags

  body = {
    properties = {
      contentType = "ADO.NET connection string"
      value       = "Server=tcp:${azurerm_mssql_server.app.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.app.name};Authentication=Active Directory Managed Identity;User Id=${azurerm_user_assigned_identity.app.client_id};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
      attributes = {
        enabled = true
      }
    }
  }

  response_export_values = []
}

resource "azurerm_role_assignment" "key_vault_reader" {
  scope                            = azurerm_key_vault.app.id
  role_definition_name             = "Key Vault Secrets User"
  principal_id                     = azurerm_user_assigned_identity.app.principal_id
  principal_type                   = "ServicePrincipal"
  skip_service_principal_aad_check = true
}

resource "azurerm_private_endpoint" "sql" {
  name                = "pe-sql-${local.resource_prefix}"
  resource_group_name = data.azurerm_resource_group.target.name
  location            = local.location
  subnet_id           = azurerm_subnet.private_endpoints.id
  tags                = local.tags

  private_service_connection {
    name                           = "sql"
    private_connection_resource_id = azurerm_mssql_server.app.id
    subresource_names              = ["sqlServer"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "sql"
    private_dns_zone_ids = [azurerm_private_dns_zone.sql.id]
  }
}

resource "azurerm_private_endpoint" "key_vault" {
  name                = "pe-kv-${local.resource_prefix}"
  resource_group_name = data.azurerm_resource_group.target.name
  location            = local.location
  subnet_id           = azurerm_subnet.private_endpoints.id
  tags                = local.tags

  private_service_connection {
    name                           = "key-vault"
    private_connection_resource_id = azurerm_key_vault.app.id
    subresource_names              = ["vault"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "key-vault"
    private_dns_zone_ids = [azurerm_private_dns_zone.key_vault.id]
  }
}

resource "azurerm_service_plan" "app" {
  name                = "asp-${local.resource_prefix}"
  resource_group_name = data.azurerm_resource_group.target.name
  location            = local.location
  os_type             = "Linux"
  sku_name            = "B1"
  worker_count        = 1
  tags                = local.tags
}

resource "azurerm_linux_web_app" "app" {
  name                                           = "app-${local.resource_prefix}"
  resource_group_name                            = data.azurerm_resource_group.target.name
  location                                       = local.location
  service_plan_id                                = azurerm_service_plan.app.id
  virtual_network_subnet_id                      = azurerm_subnet.app.id
  key_vault_reference_identity_id                = azurerm_user_assigned_identity.app.id
  https_only                                     = true
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false
  tags                                           = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  site_config {
    ip_restriction_default_action     = "Deny"
    scm_use_main_ip_restriction       = false
    scm_ip_restriction_default_action = "Allow"

    ip_restriction {
      name        = "ThisFrontDoorOnly"
      priority    = 100
      action      = "Allow"
      service_tag = "AzureFrontDoor.Backend"
      headers = [{
        x_azure_fdid      = [azurerm_cdn_frontdoor_profile.app.resource_guid]
        x_fd_health_probe = []
        x_forwarded_for   = []
        x_forwarded_host  = []
      }]
    }

    always_on                         = true
    http2_enabled                     = true
    minimum_tls_version               = "1.2"
    scm_minimum_tls_version           = "1.2"
    ftps_state                        = "Disabled"
    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 10
    vnet_route_all_enabled            = true
    use_32_bit_worker                 = false
    app_command_line                  = "dotnet DotNetCoreSqlDb.dll"

    application_stack {
      dotnet_version = "10.0"
    }
  }

  app_settings = {
    SEARCH_ENDPOINT                     = azurerm_search_service.app.endpoint
    SEARCH_INDEX_NAME                   = "todos"
    PUBLIC_ORIGIN                       = "https://${azurerm_cdn_frontdoor_endpoint.app.host_name}"
    ASPNETCORE_ENVIRONMENT              = "Production"
    SCM_DO_BUILD_DURING_DEPLOYMENT      = "false"
    WEBSITE_RUN_FROM_PACKAGE            = "1"
    WEBSITES_CONTAINER_START_TIME_LIMIT = "600"
    SQL_SERVER_FQDN                     = azurerm_mssql_server.app.fully_qualified_domain_name
    SQL_DATABASE_NAME                   = azurerm_mssql_database.app.name
    SQL_APP_CLIENT_ID                   = azurerm_user_assigned_identity.app.client_id
    SQL_APP_PRINCIPAL_NAME              = azurerm_user_assigned_identity.app.name
  }

  connection_string {
    name  = "AZURE_SQL_CONNECTIONSTRING"
    type  = "SQLAzure"
    value = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault.app.vault_uri}secrets/${azapi_resource.sql_connection.name})"
  }

  logs {
    application_logs {
      file_system_level = "Information"
    }

    http_logs {
      file_system {
        retention_in_days = 3
        retention_in_mb   = 35
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.search_documents,
    azurerm_role_assignment.key_vault_reader,
    azurerm_private_endpoint.sql,
    azurerm_private_endpoint.key_vault,
    azurerm_private_dns_zone_virtual_network_link.sql,
    azurerm_private_dns_zone_virtual_network_link.key_vault,
  ]
}
