package gitport;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Profile-local bookmarks and groups. These operations never move repository files. */
public final class RepositoryCatalog {
    public record Group(String id,String name){}
    public final Portable portable;
    public RepositoryCatalog(Portable portable){this.portable=portable;}
    public List<Group> groups(){List<Group> result=new ArrayList<>();for(String id:portable.settings.getProperty("groups","").split(","))if(!id.isBlank())result.add(new Group(id,portable.settings.getProperty("group."+id+".name",id)));result.add(new Group("ungrouped","Ungrouped"));return result;}
    public List<String> repositories(){return Arrays.stream(portable.settings.getProperty("repos","").split("\n")).filter(s -> !s.isBlank()).distinct().toList();}
    private String key(String path){return Base64.getUrlEncoder().withoutPadding().encodeToString(path.getBytes(StandardCharsets.UTF_8));}
    public String groupOf(String path){String group=portable.settings.getProperty("repo.group."+key(path),"ungrouped");return groups().stream().anyMatch(g -> g.id.equals(group))?group:"ungrouped";}
    public String nameOf(String path){String custom=portable.settings.getProperty("repo.name."+key(path),"");if(!custom.isBlank())return custom;Path file=portable.resolve(path).getFileName();return file==null?path:file.toString();}
    private void validName(String name) throws IOException{if(name==null||name.isBlank()||name.length()>80||name.chars().anyMatch(Character::isISOControl))throw new IOException("Use a name of 1-80 characters without control characters.");}
    public String createGroup(String name) throws IOException{validName(name);if(groups().stream().anyMatch(g -> g.name.equalsIgnoreCase(name.strip())))throw new IOException("That group name already exists.");String id=UUID.randomUUID().toString();String previous=portable.settings.getProperty("groups","");portable.settings.setProperty("groups",previous.isBlank()?id:previous+","+id);portable.settings.setProperty("group."+id+".name",name.strip());portable.save();return id;}
    public void renameGroup(String id,String name) throws IOException{validName(name);if(id.equals("ungrouped"))throw new IOException("The Ungrouped group cannot be renamed.");if(groups().stream().noneMatch(g -> g.id.equals(id)))throw new IOException("Unknown group.");if(groups().stream().anyMatch(g -> !g.id.equals(id)&&g.name.equalsIgnoreCase(name.strip())))throw new IOException("That group name already exists.");portable.settings.setProperty("group."+id+".name",name.strip());portable.save();}
    public void move(String path,String id) throws IOException{if(!repositories().contains(path))throw new IOException("Repository bookmark not found.");if(groups().stream().noneMatch(g -> g.id.equals(id)))throw new IOException("Unknown group.");portable.settings.setProperty("repo.group."+key(path),id);portable.save();}
    public void renameRepository(String path,String name) throws IOException{validName(name);portable.settings.setProperty("repo.name."+key(path),name.strip());portable.save();}
    public void removeGroup(String id) throws IOException{if(id.equals("ungrouped"))throw new IOException("Ungrouped cannot be removed.");for(String path:repositories())if(groupOf(path).equals(id))portable.settings.remove("repo.group."+key(path));portable.settings.setProperty("groups",String.join(",",groups().stream().filter(g -> !g.id.equals(id)&&!g.id.equals("ungrouped")).map(Group::id).toList()));portable.settings.remove("group."+id+".name");portable.settings.remove("group."+id+".collapsed");portable.save();}
    public void forget(String path) throws IOException{portable.settings.setProperty("repos",String.join("\n",repositories().stream().filter(p -> !p.equals(path)).toList()));portable.settings.remove("repo.group."+key(path));portable.settings.remove("repo.name."+key(path));if(portable.settings.getProperty("lastRepo","").equals(path))portable.settings.remove("lastRepo");portable.save();}
}
