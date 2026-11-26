resource "azurerm_cdn_frontdoor_profile" "app" {
  name                     = "afd-${local.resource_prefix}"
  resource_group_name      = data.azurerm_resource_group.target.name
  sku_name                 = "Standard_AzureFrontDoor"
  response_timeout_seconds = 180
  tags                     = local.tags
}

resource "azurerm_cdn_frontdoor_endpoint" "app" {
  name                     = "marmara-${local.suffix}"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.app.id
  enabled                  = true
  tags                     = local.tags
}

resource "azurerm_cdn_frontdoor_origin_group" "app" {
  name                     = "app"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.app.id
  session_affinity_enabled = false

  health_probe {
    interval_in_seconds = 30
    path                = "/health"
    protocol            = "Https"
    request_type        = "GET"
  }

  load_balancing {
    sample_size                 = 4
    successful_samples_required = 3
  }
}

resource "azurerm_cdn_frontdoor_origin" "app" {
  name                           = "app-service"
  cdn_frontdoor_origin_group_id  = azurerm_cdn_frontdoor_origin_group.app.id
  enabled                        = true
  host_name                      = azurerm_linux_web_app.app.default_hostname
  origin_host_header             = azurerm_linux_web_app.app.default_hostname
  http_port                      = 80
  https_port                     = 443
  certificate_name_check_enabled = true
  priority                       = 1
  weight                         = 1000
}

resource "azurerm_cdn_frontdoor_route" "app" {
  name                          = "web-app"
  cdn_frontdoor_endpoint_id     = azurerm_cdn_frontdoor_endpoint.app.id
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.app.id
  cdn_frontdoor_origin_ids      = [azurerm_cdn_frontdoor_origin.app.id]
  patterns_to_match             = ["/*"]
  supported_protocols           = ["Http", "Https"]
  forwarding_protocol           = "HttpsOnly"
  https_redirect_enabled        = true
  link_to_default_domain        = true
  enabled                       = true

  # Omitting cache keeps account pages, anti-forgery tokens and private Todos off the shared cache.
}
