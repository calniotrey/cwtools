namespace CWTools.Games.CK2
open CWTools.Localisation
open CWTools.Validation.ValidationCore
open CWTools.Games.Files
open CWTools.Games
open CWTools.Common
open FSharp.Collections.ParallelSeq
open CWTools.Localisation.CK2Localisation
open CWTools.Utilities.Utils
open CWTools.Utilities.Position
open CWTools.Utilities
open System.IO
open CWTools.Validation.Common.CommonValidation
// open CWTools.Validation.Rules
open CWTools.Parser.ConfigParser
open CWTools.Common.CK2Constants
open CWTools.Validation.Rules
open CWTools.Process.CK2Scopes
open CWTools.Common
open CWTools.Process.Scopes
open CWTools.Validation.CK2
open System.Text
open CWTools.Validation.Rules
open CWTools.Games.LanguageFeatures
open CWTools.Validation.CK2.CK2LocalisationString
open CWTools.Validation.LocalisationString
open CWTools.Process
open CWTools.Process.ProcessCore
open System

module CK2GameFunctions =
    type GameObject = GameObject<Scope, Modifier, CK2ComputedData>
    let processLocalisationFunction (localisationCommands : ((string * Scope list) list)) (lookup : Lookup<Scope, Modifier>) =
        let eventtargets =
            (lookup.varDefInfo.TryFind "event_target" |> Option.defaultValue [] |> List.map fst)
        let definedvars =
            (lookup.varDefInfo.TryFind "variable" |> Option.defaultValue [] |> List.map fst)
        processLocalisation localisationCommands eventtargets lookup.scriptedLoc definedvars

    let validateLocalisationCommandFunction (localisationCommands : ((string * Scope list) list)) (lookup : Lookup<Scope, Modifier>) =
        let eventtargets =
            (lookup.varDefInfo.TryFind "event_target" |> Option.defaultValue [] |> List.map fst)
        let definedvars =
            (lookup.varDefInfo.TryFind "variable" |> Option.defaultValue [] |> List.map fst)
        validateLocalisationCommand localisationCommands eventtargets lookup.scriptedLoc definedvars

    let globalLocalisation (game : GameObject) =
        // let locfiles =  resources.GetResources()
        //                 |> List.choose (function |FileWithContentResource (_, e) -> Some e |_ -> None)
        //                 |> List.filter (fun f -> f.overwrite <> Overwritten && f.extension = ".yml" && f.validate)
        //                 |> List.map (fun f -> f.filepath)
        // let locFileValidation = validateLocalisationFiles locfiles
        let locParseErrors = game.LocalisationManager.LocalisationAPIs() <&!&> (fun (b, api) -> if b then validateLocalisationSyntax api.Results else OK)
        let globalTypeLoc = game.ValidationManager.ValidateGlobalLocalisation()
        game.Lookup.proccessedLoc |> validateProcessedLocalisation game.LocalisationManager.taggedLocalisationKeys <&&>
        locParseErrors <&&>
        globalTypeLoc |> (function |Invalid es -> es |_ -> [])
    let updateScriptedLoc (game : GameObject) =
        let rawLocs =
            game.Resources.AllEntities()
            |> List.choose (function |struct (f, _) when f.filepath.Contains("customizable_localisation") -> Some (f.entity) |_ -> None)
            |> List.collect (fun n -> n.Children)
            |> List.map (fun l -> l.TagText "name")
        game.Lookup.scriptedLoc <- rawLocs

    let updateModifiers (game : GameObject) =
        game.Lookup.coreModifiers <- game.Settings.embedded.modifiers

    let addModifiersWithScopes (lookup : Lookup<_,_>) =
        let modifierOptions (modifier : Modifier) =
            let requiredScopes =
                modifier.categories |> List.choose (fun c -> modifierCategoryToScopesMap.TryFind c)
                                    |> List.map Set.ofList
                                    |> (fun l -> if List.isEmpty l then [] else l |> List.reduce (Set.intersect) |> Set.toList)
            {min = 0; max = 100; leafvalue = false; description = None; pushScope = None; replaceScopes = None; severity = None; requiredScopes = requiredScopes}
        lookup.coreModifiers
            |> List.map (fun c -> AliasRule ("modifier", NewRule(LeafRule(specificField c.tag, ValueField (ValueType.Float (-1E+12, 1E+12))), modifierOptions c)))

    let updateLandedTitles (game : GameObject) =
        let fNode =
            fun (t : Node) result ->
                match t.Key with
                | x when x.StartsWith "e_" && t.TagText "landless" == "yes" -> ((Empire, true), x)::result
                | x when x.StartsWith "e_" -> ((Empire, false), x)::result
                | x when x.StartsWith "k_" && t.TagText "landless" == "yes" -> ((Kingdom, true), x)::result
                | x when x.StartsWith "k_" -> ((Kingdom, false), x)::result
                | x when x.StartsWith "d_" ->
                    match t.TagText "landless", t.TagText "mercenary", t.TagText "holy_order" with
                    | "yes", "yes", _
                    | "yes", _, "yes" ->
                        ((Duchy_Hired, true), x)::result
                    | "yes", _, _ ->
                        ((Duchy_Normal, true), x)::result
                    | _, "yes", _
                    | _, _, "yes" ->
                        ((Duchy_Hired, false), x)::result
                    | _ ->
                        ((Duchy_Normal, false), x)::result
                | x when x.StartsWith "c_" && t.TagText "landless" == "yes" -> ((County, true), x)::result
                | x when x.StartsWith "c_" -> ((County, false), x)::result
                | x when x.StartsWith "b_" && t.TagText "landless" == "yes"-> ((Barony, true), x)::result
                | x when x.StartsWith "b_" -> ((Barony, false), x)::result
                | _ -> result
        let titleEntities = (EntitySet (game.Resources.AllEntities())).GlobMatchChildren("**/landed_titles/**/*.txt")
        let titles = titleEntities |> List.collect (foldNode7 fNode)
        let inner (ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs) (k : TitleType * bool, value : string) =
             match k with
             | Empire, true -> (value::ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs)
             | Empire, false -> (ells, value::es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs)
             | Kingdom, true -> (ells, es, value::klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs)
             | Kingdom, false -> (ells, es, klls, value::ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs)
             | Duchy_Normal, true -> (ells, es, klls, ks, value::dnlls, dns, dhlls, dhs, clls, cs, blls, bs)
             | Duchy_Normal, false -> (ells, es, klls, ks, dnlls, value::dns, dhlls, dhs, clls, cs, blls, bs)
             | Duchy_Hired, true -> (ells, es, klls, ks, dnlls, dns, value::value::dhlls, dhs, clls, cs, blls, bs)
             | Duchy_Hired, false -> (ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs)
             | County, true -> (ells, es, klls, ks, dnlls, dns, dhlls, dhs,value:: clls, cs, blls, bs)
             | County, false -> (ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, value::cs, blls, bs)
             | Barony, true -> (ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, value::blls, bs)
             | Barony, false -> (ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, value::bs)
        let (ells, es, klls, ks, dnlls, dns, dhlls, dhs, clls, cs, blls, bs) = titles |> List.fold inner ([], [], [], [], [], [], [], [], [], [], [], [])
        game.Lookup.CK2LandedTitles <-
            ((Empire, true), ells)::((Empire, false), es)::((Kingdom, true), klls)::((Kingdom, false), ks)
                ::((Duchy_Normal, true), dnlls)::((Duchy_Normal, false), dns)
                ::((Duchy_Hired, true), dhlls)::((Duchy_Hired, false), dhs)
                ::((County, true), clls)::((County, false), cs)
                ::((Barony, true), blls)::[((Barony, false), bs)] |> Map.ofList
    let createLandedTitleTypes(lookup : Lookup<_,_>)(map : Map<_,_>) =
        let ells = lookup.CK2LandedTitles.[Empire, true] |> List.map (fun e -> (false, e, range.Zero))
        let es = lookup.CK2LandedTitles.[Empire, false] |> List.map (fun e -> (false, e, range.Zero))
        let klls = lookup.CK2LandedTitles.[Kingdom, true] |> List.map (fun e -> (false, e, range.Zero))
        let ks = lookup.CK2LandedTitles.[Kingdom, false] |> List.map (fun e -> (false, e, range.Zero))
        let dllns = lookup.CK2LandedTitles.[Duchy_Normal, true] |> List.map (fun e -> (false, e, range.Zero))
        let dns = lookup.CK2LandedTitles.[Duchy_Normal, false] |> List.map (fun e -> (false, e, range.Zero))
        let dllhs = lookup.CK2LandedTitles.[Duchy_Hired, true] |> List.map (fun e -> (false, e, range.Zero))
        let dhs = lookup.CK2LandedTitles.[Duchy_Hired, false] |> List.map (fun e -> (false, e, range.Zero))
        let clls = lookup.CK2LandedTitles.[County, true] |> List.map (fun e -> (false, e, range.Zero))
        let cs = lookup.CK2LandedTitles.[County, false] |> List.map (fun e -> (false, e, range.Zero))
        let blls = lookup.CK2LandedTitles.[Barony, true] |> List.map (fun e -> (false, e, range.Zero))
        let bs = lookup.CK2LandedTitles.[Barony, false] |> List.map (fun e -> (false, e, range.Zero))
        map |> Map.add "title.empire" (es@ells)
            |> Map.add "title.kingdom" (ks@klls)
            |> Map.add "title.duchy" (dllns@dns@dllhs@dhs)
            |> Map.add "title.duchy_hired" (dllhs@dhs)
            |> Map.add "title.duchy_normal" (dllns@dns)
            |> Map.add "title.county" (clls@cs)
            |> Map.add "title.barony" (blls@bs)
            |> Map.add "title.landless" (ells@klls@dllns@dllhs@clls@blls)
            |> Map.add "title.landed" (es@ks@dns@dhs@cs@bs)
            |> Map.add "title" (es@ells@ks@klls@dns@dllns@dhs@dllhs@cs@clls@bs@blls)

    let updateProvinces (game : GameObject) =
        let provinceFile =
            game.Resources.GetResources()
            |> List.choose (function |FileWithContentResource (_, e) -> Some e |_ -> None)
            |> List.tryFind (fun f -> f.overwrite <> Overwritten && Path.GetFileName(f.filepath) = "definition.csv")
        match provinceFile with
        |None -> ()
        |Some pf ->
            let lines = pf.filetext.Split(([|"\r\n"; "\r"; "\n"|]), StringSplitOptions.None)
            let provinces = lines |> Array.choose (fun l -> l.Split([|';'|], 2, StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead) |> List.ofArray
            game.Lookup.CK2provinces <- provinces

    let updateScriptedEffects (lookup : Lookup<_,_>) (rules :RootRule<Scope> list) (embeddedSettings : EmbeddedSettings<_,_>) =
        let effects =
            rules |> List.choose (function |AliasRule("effect", r) -> Some r |_ -> None)
        let ruleToEffect(r,o) =
            let name =
                match r with
                |LeafRule(ValueField(Specific n),_) -> StringResource.stringManager.GetStringForID n.normal
                |NodeRule(ValueField(Specific n),_) -> StringResource.stringManager.GetStringForID n.normal
                |_ -> ""
            DocEffect(name, o.requiredScopes, EffectType.Effect, o.description |> Option.defaultValue "", "")
        // let simpleEventTargetLinks = embeddedSettings.eventTargetLinks |> List.choose (function | SimpleLink l -> Some (l :> Effect) | _ -> None)
        (effects |> List.map ruleToEffect  |> List.map (fun e -> e :> Effect))

    let updateScriptedTriggers (lookup : Lookup<_,_>) (rules :RootRule<Scope> list) (embeddedSettings : EmbeddedSettings<_,_>) =
        let effects =
            rules |> List.choose (function |AliasRule("trigger", r) -> Some r |_ -> None)
        let ruleToTrigger(r,o) =
            let name =
                match r with
                |LeafRule(ValueField(Specific n),_) -> StringResource.stringManager.GetStringForID n.normal
                |NodeRule(ValueField(Specific n),_) -> StringResource.stringManager.GetStringForID n.normal
                |_ -> ""
            DocEffect(name, o.requiredScopes, EffectType.Trigger, o.description |> Option.defaultValue "", "")
        // let simpleEventTargetLinks = embeddedSettings.eventTargetLinks |> List.choose (function | SimpleLink l -> Some (l :> Effect) | _ -> None)
        (effects |> List.map ruleToTrigger |> List.map (fun e -> e :> Effect))

    let updateEventTargetLinks (embeddedSettings : EmbeddedSettings<_,_>) =
        let simpleEventTargetLinks = embeddedSettings.eventTargetLinks |> List.choose (function | SimpleLink l -> Some (l :> Effect) | _ -> None)
        simpleEventTargetLinks
    let addDataEventTargetLinks (lookup : Lookup<'S,'M>) (embeddedSettings : EmbeddedSettings<_,_>) =
        let links = embeddedSettings.eventTargetLinks |> List.choose (function | DataLink l -> Some (l) | _ -> None)
        let convertLinkToEffects (link : EventTargetDataLink<_>) =
            let typeDefinedKeys = lookup.typeDefInfo.[link.sourceRuleType] |> List.map fst
            let keyToEffect (key : string) =
                let key = link.dataPrefix |> Option.map ((+) key) |> Option.defaultValue key
                ScopedEffect(key, link.inputScopes, link.outputScope, EffectType.Both, link.description, "", true)
            typeDefinedKeys |> List.map keyToEffect
        links |> List.collect convertLinkToEffects |> List.map (fun e -> e :> Effect)

    let addModifiersAsTypes (lookup : Lookup<_,_>) (typesMap : Map<string,(bool * string * range) list>) =
        // let createType (modifier : Modifier) =
        typesMap.Add("modifier", lookup.coreModifiers |> List.map (fun m -> (false, m.tag, range.Zero)))


    let loadConfigRulesHook rules (lookup : Lookup<_,_>) embedded =
        lookup.triggers <- updateScriptedTriggers lookup rules embedded
        lookup.effects <- updateScriptedEffects lookup rules embedded
        lookup.eventTargetLinks <- updateEventTargetLinks embedded
        rules @ addModifiersWithScopes lookup

    let refreshConfigBeforeFirstTypesHook (lookup : Lookup<_,_>) _ _ =
        let modifierEnums = { key = "modifiers"; values = lookup.coreModifiers |> List.map (fun m -> m.Tag); description = "Modifiers" }
        let provinceEnums = { key = "provinces"; description = "provinces"; values = lookup.CK2provinces}
        lookup.enumDefs <-
            lookup.enumDefs |> Map.add modifierEnums.key (modifierEnums.description, modifierEnums.values)
                            |> Map.add provinceEnums.key (provinceEnums.description, provinceEnums.values)

    let refreshConfigAfterFirstTypesHook (lookup : Lookup<_,_>) _ (embeddedSettings : EmbeddedSettings<_,_>) =
        lookup.typeDefInfoRaw <-
            createLandedTitleTypes lookup (lookup.typeDefInfoRaw)
            |> addModifiersAsTypes lookup
        lookup.eventTargetLinks <- updateEventTargetLinks embeddedSettings @ addDataEventTargetLinks lookup embeddedSettings

    let afterInit (game : GameObject) =
        // updateScriptedTriggers()
        // updateScriptedEffects()
        // updateStaticodifiers()
        // updateScriptedLoc(game)
        // updateDefinedVariables()
        updateModifiers(game)
        // updateLegacyGovernments(game)
        // updateTechnologies()
        updateLandedTitles(game)
        updateProvinces(game)
        // game.LocalisationManager.UpdateAllLocalisation()
        // updateTypeDef game game.Settings.rules
        // game.LocalisationManager.UpdateAllLocalisation()
type CK2Settings = GameSettings<Modifier, Scope>
open CK2GameFunctions
type CK2Game(settings : CK2Settings) =
    let validationSettings = {
        validators = [ validateMixedBlocks, "mixed";]
        experimentalValidators = []
        heavyExperimentalValidators = []
        experimental = false
        fileValidators = []
        lookupValidators = []
        useRules = true
        debugRulesOnly = false
        localisationValidators = []
    }

    let settings = { settings with embedded = { settings.embedded with localisationCommands = settings.embedded.localisationCommands |> (fun l -> if l.Length = 0 then locCommands else l )}}

    let rulesManagerSettings = {
        rulesSettings = settings.rules
        parseScope = parseScope
        allScopes = allScopes
        anyScope = Scope.Any
        changeScope = changeScope
        defaultContext = defaultContext
        defaultLang = CK2 CK2Lang.Default
        oneToOneScopesNames = oneToOneScopesNames
        loadConfigRulesHook = loadConfigRulesHook
        refreshConfigBeforeFirstTypesHook = refreshConfigBeforeFirstTypesHook
        refreshConfigAfterFirstTypesHook = refreshConfigAfterFirstTypesHook
    }
    let game = GameObject<Scope, Modifier, CK2ComputedData>.CreateGame
                ((settings, "crusader kings ii", scriptFolders, CK2Compute.computeCK2Data,
                    CK2Compute.computeCK2DataUpdate,
                     (CK2LocalisationService >> (fun f -> f :> ILocalisationAPICreator)),
                     CK2GameFunctions.processLocalisationFunction (settings.embedded.localisationCommands),
                     CK2GameFunctions.validateLocalisationCommandFunction (settings.embedded.localisationCommands),
                     defaultContext,
                     noneContext,
                     Encoding.UTF8,
                     Encoding.GetEncoding(1252),
                     validationSettings,
                     globalLocalisation,
                     (fun _ _ -> ()),
                     ".csv",
                     rulesManagerSettings))
                     afterInit
    let lookup = game.Lookup
    let resources = game.Resources
    let fileManager = game.FileManager


    let parseErrors() =
        resources.GetResources()
            |> List.choose (function |EntityResource (_, e) -> Some e |_ -> None)
            |> List.choose (fun r -> r.result |> function |(Fail (result)) when r.validate -> Some (r.filepath, result.error, result.position)  |_ -> None)

    interface IGame<CK2ComputedData, Scope, Modifier> with
    //member __.Results = parseResults
        member __.ParserErrors() = parseErrors()
        member __.ValidationErrors() = let (s, d) = (game.ValidationManager.Validate(false, (resources.ValidatableEntities()))) in s @ d
        member __.LocalisationErrors(force : bool, forceGlobal : bool) =
            let genGlobal() =
                let ges = (globalLocalisation(game))
                game.LocalisationManager.globalLocalisationErrors <- Some ges
                ges
            let genAll() =
                let les = (game.ValidationManager.ValidateLocalisation (resources.ValidatableEntities()))
                game.LocalisationManager.localisationErrors <- Some les
                les
            match game.LocalisationManager.localisationErrors, game.LocalisationManager.globalLocalisationErrors with
            |Some les, Some ges -> (if force then genAll() else les) @ (if forceGlobal then genGlobal() else ges)
            |None, Some ges -> (genAll()) @ (if forceGlobal then genGlobal() else ges)
            |Some les, None -> (if force then genAll() else les) @ (genGlobal())
            |None, None -> (genAll()) @ (genGlobal())

        //member __.ValidationWarnings = warningsAll
        member __.Folders() = fileManager.AllFolders()
        member __.AllFiles() =
            resources.GetResources()
        member __.AllLoadedLocalisation() = game.LocalisationManager.LocalisationFileNames()
            // |> List.map
            //     (function
            //         |EntityResource (f, r) ->  r.result |> function |(Fail (result)) -> (r.filepath, false, result.parseTime) |Pass(result) -> (r.filepath, true, result.parseTime)
            //         |FileResource (f, r) ->  (r.filepath, false, 0L))
            //|> List.map (fun r -> r.result |> function |(Fail (result)) -> (r.filepath, false, result.parseTime) |Pass(result) -> (r.filepath, true, result.parseTime))
        member __.ScriptedTriggers() = lookup.triggers
        member __.ScriptedEffects() = lookup.effects
        member __.StaticModifiers() = [] //lookup.staticModifiers
        member __.UpdateFile shallow file text =game.UpdateFile shallow file text
        member __.AllEntities() = resources.AllEntities()
        member __.References() = References<_, Scope, _>(resources, lookup, (game.LocalisationManager.LocalisationAPIs() |> List.map snd))
        member __.Complete pos file text = completion fileManager game.completionService game.InfoService game.ResourceManager pos file text
        member __.ScopesAtPos pos file text = scopesAtPos fileManager game.ResourceManager game.InfoService Scope.Any pos file text |> Option.map (fun sc -> { OutputScopeContext.From = sc.From; Scopes = sc.Scopes; Root = sc.Root})
        member __.GoToType pos file text = getInfoAtPos fileManager game.ResourceManager game.InfoService lookup pos file text
        member __.FindAllRefs pos file text = findAllRefsFromPos fileManager game.ResourceManager game.InfoService pos file text
        member __.InfoAtPos pos file text = game.InfoAtPos pos file text
        member __.ReplaceConfigRules rules = game.ReplaceConfigRules(({ ruleFiles = rules; validateRules = true; debugRulesOnly = false; debugMode = false})) //refreshRuleCaches game (Some { ruleFiles = rules; validateRules = true; debugRulesOnly = false; debugMode = false})
        member __.RefreshCaches() = game.RefreshCaches()
        member __.RefreshLocalisationCaches() = game.LocalisationManager.UpdateProcessedLocalisation()
        member __.ForceRecompute() = resources.ForceRecompute()
        member __.Types() = game.Lookup.typeDefInfo

            //member __.ScriptedTriggers = parseResults |> List.choose (function |Pass(f, p, t) when f.Contains("scripted_triggers") -> Some p |_ -> None) |> List.map (fun t -> )