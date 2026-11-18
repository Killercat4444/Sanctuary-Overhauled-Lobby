local policy = Import("overhauledlobby/restrictions.lua")
if policy.Enabled then
    local originalList, originalCheck = buildQueueUtils.GetBuildableTags, buildQueueUtils.CanBuild
    buildQueueUtils.GetBuildableTags = function(builder)
        local source = originalList(builder)
        if type(source) ~= "table" then return source end
        local permitted = {}
        for id,value in pairs(source) do
            if not policy.Denied(id) then permitted[id] = value end
        end
        return setmetatable(permitted, getmetatable(source))
    end
    buildQueueUtils.CanBuild = function(builder,id,...)
        if policy.Denied(id) then return false end
        return originalCheck(builder,id,...)
    end
end
