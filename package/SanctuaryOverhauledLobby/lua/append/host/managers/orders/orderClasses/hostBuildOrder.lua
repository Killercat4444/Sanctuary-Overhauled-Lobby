local policy = Import("overhauledlobby/restrictions.lua")
if policy.Enabled then
    local validate = HostBuildOrder.IsOrderValid
    HostBuildOrder.IsOrderValid = function(order,...)
        return not policy.Denied(order.tpId) and validate(order,...)
    end
end
