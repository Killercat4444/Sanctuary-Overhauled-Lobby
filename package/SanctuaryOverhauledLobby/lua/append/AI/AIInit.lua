local policy = Import("overhauledlobby/restrictions.lua")
if policy.Enabled then
    local initialize = InitAIInfrastructure
    InitAIInfrastructure = function(...)
        for _,army in pairs(Armies) do
            if army.lobbyOptions then policy.ConfigureAI(army.lobbyOptions.aiSettings) end
        end
        return initialize(...)
    end
    -- Native AIInit captures this local helper at line86. Appended code shares
    -- its scope. Change fresh defaults BEFORE takeover constructs strategy
    -- pools/starts threads, not after InitAIInfrastructureForPlayer returns.
    local defaults = GetDefaultAISettings
    GetDefaultAISettings = function(...)
        local result = defaults(...)
        if result then policy.ConfigureAI(result.aiSettings) end
        return result
    end
end
