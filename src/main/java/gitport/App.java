package gitport;

import javax.swing.*;
import javax.swing.border.*;
import javax.swing.table.*;
import java.awt.*;
import java.awt.event.*;
import java.io.*;
import java.nio.channels.*;
import java.nio.charset.*;
import java.nio.file.*;
import java.util.*;
import java.util.List;
import java.util.concurrent.*;
import java.util.function.*;
import java.util.regex.*;

public final class App extends JFrame {
    private static final Color BG=AtomTheme.TREE, SURFACE=AtomTheme.EDITOR, TEXT=AtomTheme.TEXT, BLUE=AtomTheme.BLUE, BORDER=AtomTheme.BORDER;
    private final Portable portable;
    private Git git;
    private final ExecutorService jobs=Executors.newSingleThreadExecutor(r -> {Thread t=new Thread(r,"GitPort-worker"); t.setDaemon(true); return t;});
    private final DefaultListModel<String> branchModel=new DefaultListModel<>();
    private final JList<String> branches=new JList<>(branchModel);
    private BranchExplorer branchExplorer;
    private GitLogView gitLog;
    private final CardLayout sidebarCards=new CardLayout();
    private final JPanel sidebarPanel=new JPanel(sidebarCards);
    
    private final DefaultTableModel filesModel=new DefaultTableModel(new String[]{"State","Path"},0) { public boolean isCellEditable(int r,int c) {return false;} };
    private final JTable files=new JTable(filesModel);
    private final DiffView diff=new DiffView();
    private final JTextArea history=area(), output=area(), message=area();
    private ProjectExplorer explorer;
    private final JLabel repoPath=new JLabel(),fileTitle=new JLabel(),changeCountLabel=new JLabel("0");
    private JSplitPane activitySplit;
    private final JLabel status=new JLabel("Open a repository to begin"), repoTitle=new JLabel("No repository selected"), branchTitle=new JLabel("LOCAL WORKSPACE");
    private final JCheckBox stagedDiff=new JCheckBox("Staged diff");
    private final List<AbstractButton> actions=new ArrayList<>();
    private List<Git.Change> changes=List.of();
    private boolean busy;
    private String sessionToken="";
    private final Auth auth;
    private final FileChannel instanceChannel;
    private final FileLock instanceLock;
    private final JTabbedPane tabs=new JTabbedPane();

    public App(Portable portable) throws Exception {
        super("GitPort · Portable Git workspace"); this.portable=portable;
        instanceChannel=FileChannel.open(portable.root.resolve("data/app.lock"),StandardOpenOption.CREATE,StandardOpenOption.WRITE);
        instanceLock=instanceChannel.tryLock(); if(instanceLock==null) throw new IOException("GitPort is already running from this folder.");
        auth=new Auth(this::askCredential);
        setDefaultCloseOperation(DO_NOTHING_ON_CLOSE);
        addWindowListener(new WindowAdapter(){public void windowClosing(WindowEvent e){
            if(busy) { JOptionPane.showMessageDialog(App.this,"A Git operation is still running. Let it finish before closing."); return; }
            try {auth.close(); instanceLock.release(); instanceChannel.close();}catch(Exception ignored){}
            jobs.shutdown(); dispose();
        }});
        Rectangle available=GraphicsEnvironment.getLocalGraphicsEnvironment().getMaximumWindowBounds();
        int initialWidth=Math.min(1460,Math.max(800,available.width-32)),initialHeight=Math.min(920,Math.max(560,available.height-32));
        setMinimumSize(new Dimension(Math.min(1000,initialWidth),Math.min(640,initialHeight)));setSize(initialWidth,initialHeight);setLocationRelativeTo(null);
        setIconImages(Brand.windowIcons());
        JPanel canvas=new JPanel(new BorderLayout());canvas.setBackground(AtomTheme.EDITOR);setContentPane(canvas);
        JPanel header=new JPanel(new BorderLayout(18,0));header.setBackground(AtomTheme.HEADER);header.setBorder(new EmptyBorder(10,16,10,16));
        JPanel identity=new JPanel(new FlowLayout(FlowLayout.LEFT,9,0));identity.setOpaque(false);identity.add(new JLabel(Brand.icon(30)));
        JLabel wordmark=new JLabel("GitPort");wordmark.setFont(AtomTheme.UI.deriveFont(Font.BOLD,19f));wordmark.setForeground(AtomTheme.BRIGHT);identity.add(wordmark);
        JLabel version=new JLabel("  0.2  ");version.setFont(AtomTheme.UI.deriveFont(12f));version.setForeground(AtomTheme.SECONDARY);identity.add(version);header.add(identity,BorderLayout.WEST);
        JLabel portableLabel=new JLabel("PORTABLE WORKSPACE");portableLabel.setFont(AtomTheme.UI.deriveFont(Font.BOLD,12f));portableLabel.setForeground(AtomTheme.MUTED);header.add(portableLabel,BorderLayout.CENTER);
        JPanel global=new JPanel(new FlowLayout(FlowLayout.RIGHT,7,0));global.setOpaque(false);
        String profileLabel=portable.profileName();JButton profileButton=button(profileLabel.length()>24?profileLabel.substring(0,23)+"...":profileLabel,this::profiles);profileButton.setToolTipText(profileLabel);profileButton.setIcon(Icons.get("profile",AtomTheme.BLUE,17));global.add(profileButton);
        JButton settingsButton=button("Settings",this::settings);settingsButton.setIcon(Icons.get("settings",AtomTheme.SECONDARY,17));global.add(settingsButton);
        JButton addRepository=button("Open repository",this::chooseRepo);addRepository.setIcon(Icons.get("plus",AtomTheme.TEXT,16));global.add(addRepository);header.add(global,BorderLayout.EAST);canvas.add(header,BorderLayout.NORTH);

        JPanel rail=new JPanel(new BorderLayout());rail.setBackground(AtomTheme.RAIL);rail.setPreferredSize(new Dimension(51,500));rail.setBorder(new MatteBorder(0,0,0,1,AtomTheme.BORDER));
        JPanel railTop=new JPanel(new GridLayout(0,1,0,7));railTop.setOpaque(false);railTop.setBorder(new EmptyBorder(10,5,0,5));
        railTop.add(railButton("branch","Branches & Worktrees",() -> showSidebar("branches")));
        railTop.add(railButton("repo","Project explorer",() -> showSidebar("projects")));
        railTop.add(railButton("history","Git Log",() -> tabs.setSelectedIndex(0)));
        railTop.add(railButton("diff","Working tree",() -> tabs.setSelectedIndex(1)));
        railTop.add(railButton("terminal","Toggle activity panel",this::toggleActivity));rail.add(railTop,BorderLayout.NORTH);
        JPanel railBottom=new JPanel(new GridLayout(0,1,0,7));railBottom.setOpaque(false);railBottom.setBorder(new EmptyBorder(0,5,12,5));railBottom.add(railButton("profile","Profiles",this::profiles));railBottom.add(railButton("settings","Settings",this::settings));rail.add(railBottom,BorderLayout.SOUTH);

        explorer=new ProjectExplorer(portable,this::open,this::chooseRepo,text -> JOptionPane.showMessageDialog(this,text,"Project explorer",JOptionPane.WARNING_MESSAGE));
        branchExplorer=new BranchExplorer(
            this::open,
            this::switchBranchTo,
            this::newBranchFrom,
            this::newWorktreeFrom,
            this::mergeBranch,
            this::deleteBranchRef,
            this::removeWorktree,
            () -> network("fetch","--prune"),
            this::pickNativeFolder,
            this::log,
            msg -> JOptionPane.showMessageDialog(this,msg,"Worktree",JOptionPane.WARNING_MESSAGE)
        );
        sidebarPanel.add(branchExplorer,"branches");
        sidebarPanel.add(explorer,"projects");
        sidebarPanel.setPreferredSize(new Dimension(285,500));
        sidebarPanel.setMinimumSize(new Dimension(230,200));

        gitLog=new GitLogView(new GitLogView.Callbacks(){
            public void checkout(String ref){switchBranchTo(ref);}
            public void newBranch(String baseRef){newBranchFrom(baseRef);}
            public void newWorktree(String baseRef){newWorktreeFrom(baseRef);}
            public void merge(String ref,boolean fetchFirst){mergeBranch(ref,fetchFirst);}
            public void pull(String ref,boolean rebase){pullBranch(ref,rebase);}
            public void rebase(String ref){rebaseOnto(ref);}
            public void compare(String ref){compareWith(ref);}
            public void deleteBranch(String ref){deleteBranchRef(ref);}
            public void showDiff(String commitHash,String file){showCommitDiff(commitHash,file);}
            public void pushUpTo(String commitHash){pushUpToCommit(commitHash);}
            public void newTag(String commitHash){newTagAt(commitHash);}
            public void commitWorkingTree(){tabs.setSelectedIndex(1);}
            public void stashWorkingTree(){stashWorkspace();}
            public void log(String msg){App.this.log(msg);}
            public void error(String msg){JOptionPane.showMessageDialog(App.this,msg,"Log Error",JOptionPane.ERROR_MESSAGE);}
        });

        JPanel editor=new JPanel(new BorderLayout());editor.setBackground(AtomTheme.EDITOR);
        JPanel repositoryHeader=new JPanel(new BorderLayout(10,0));repositoryHeader.setBackground(AtomTheme.EDITOR);repositoryHeader.setBorder(new EmptyBorder(13,17,12,14));
        JPanel location=new JPanel(new GridLayout(0,1,0,4));location.setOpaque(false);repoTitle.setText("Your repositories, organized");repoTitle.setFont(AtomTheme.UI.deriveFont(Font.BOLD,16f));repoTitle.setForeground(AtomTheme.BRIGHT);location.add(repoTitle);repoPath.setText("Open a repository to start reviewing changes");repoPath.setFont(AtomTheme.UI.deriveFont(12f));repoPath.setForeground(AtomTheme.SECONDARY);location.add(repoPath);repositoryHeader.add(location,BorderLayout.CENTER);
        JPanel sync=new JPanel(new FlowLayout(FlowLayout.RIGHT,6,0));sync.setOpaque(false);
        sync.add(actionButton("Refresh","refresh",this::refresh));sync.add(actionButton("Fetch","fetch",() -> network("fetch","--prune")));sync.add(actionButton("Pull","pull",() -> network("pull","--ff-only","--no-autostash")));sync.add(actionButton("Push","push",() -> network("push")));
        JButton more=actionButton("","more",this::repositoryMenu);more.setToolTipText("Merge, saved edits and recovery");sync.add(more);repositoryHeader.add(sync,BorderLayout.EAST);editor.add(repositoryHeader,BorderLayout.NORTH);

        JPanel changesPanel=new JPanel(new BorderLayout());changesPanel.setBackground(AtomTheme.EDITOR);
        JPanel filePane=new JPanel(new BorderLayout());filePane.setBackground(AtomTheme.TREE);filePane.setMinimumSize(new Dimension(290,200));
        JPanel fileToolbar=new JPanel(new BorderLayout());fileToolbar.setBackground(AtomTheme.TREE);fileToolbar.setBorder(new EmptyBorder(11,12,10,10));
        JPanel fileHeading=new JPanel(new FlowLayout(FlowLayout.LEFT,7,0));fileHeading.setOpaque(false);fileHeading.add(sectionLabel("CHANGED FILES"));changeCountLabel.setForeground(AtomTheme.SECONDARY);changeCountLabel.setFont(AtomTheme.UI.deriveFont(Font.BOLD,12f));fileHeading.add(changeCountLabel);fileToolbar.add(fileHeading,BorderLayout.CENTER);
        JPanel staging=new JPanel(new FlowLayout(FlowLayout.RIGHT,3,0));staging.setOpaque(false);JButton stageButton=actionButton("","plus",() -> stage(false));stageButton.setToolTipText("Stage selected files");JButton unstageButton=actionButton("","minus",() -> stage(true));unstageButton.setToolTipText("Unstage selected files");staging.add(stageButton);staging.add(unstageButton);fileToolbar.add(staging,BorderLayout.EAST);filePane.add(fileToolbar,BorderLayout.NORTH);
        files.setRowHeight(31);files.setShowGrid(false);files.setBackground(AtomTheme.TREE);files.setSelectionMode(ListSelectionModel.MULTIPLE_INTERVAL_SELECTION);files.setAutoCreateRowSorter(true);files.getTableHeader().setReorderingAllowed(false);files.getColumnModel().getColumn(0).setMaxWidth(54);files.getColumnModel().getColumn(0).setPreferredWidth(48);
        files.setDefaultRenderer(Object.class,new DefaultTableCellRenderer(){public Component getTableCellRendererComponent(JTable t,Object value,boolean selected,boolean focus,int row,int column){JLabel label=(JLabel)super.getTableCellRendererComponent(t,value,selected,focus,row,column);label.setFont(column==0?AtomTheme.CODE.deriveFont(Font.BOLD,12f):AtomTheme.UI.deriveFont(13f));label.setBorder(new EmptyBorder(5,9,5,7));String xy=(String)t.getValueAt(row,0);Color color=xy.contains("U")?AtomTheme.RED:xy.equals("??")||xy.contains("A")?AtomTheme.GREEN:xy.contains("D")?AtomTheme.RED:AtomTheme.GOLD;label.setForeground(selected?AtomTheme.BRIGHT:column==0?color:AtomTheme.TEXT);if(column==1)label.setIcon(Icons.get("file",selected?AtomTheme.BRIGHT:color,15));else label.setIcon(null);label.setToolTipText(column==1?value.toString():"Git status: "+xy);return label;}});
        filePane.add(scroll(files),BorderLayout.CENTER);
        JPanel commitPanel=new JPanel(new BorderLayout(0,9));commitPanel.setBackground(AtomTheme.TREE);commitPanel.setBorder(new CompoundBorder(new MatteBorder(1,0,0,0,AtomTheme.BORDER),new EmptyBorder(12,12,12,12)));commitPanel.add(sectionLabel("COMMIT MESSAGE"),BorderLayout.NORTH);
        message.setEditable(true);message.setFont(AtomTheme.UI);message.setRows(4);message.setLineWrap(true);message.setWrapStyleWord(true);message.setBackground(AtomTheme.EDITOR);message.setBorder(new EmptyBorder(9,10,9,10));message.setToolTipText("Describe this commit. Only staged files will be committed.");commitPanel.add(scroll(message),BorderLayout.CENTER);
        JPanel commitFooter=new JPanel(new BorderLayout(6,0));commitFooter.setOpaque(false);JLabel commitHint=new JLabel("Only staged changes");commitHint.setFont(AtomTheme.UI.deriveFont(12f));commitHint.setForeground(AtomTheme.SECONDARY);commitFooter.add(commitHint,BorderLayout.CENTER);JButton commitButton=button("Commit","check",this::commit,true);commitFooter.add(commitButton,BorderLayout.EAST);commitPanel.add(commitFooter,BorderLayout.SOUTH);filePane.add(commitPanel,BorderLayout.SOUTH);

        JPanel preview=new JPanel(new BorderLayout());preview.setBackground(AtomTheme.EDITOR);JPanel previewHeader=new JPanel(new BorderLayout(8,0));previewHeader.setBackground(AtomTheme.HEADER);previewHeader.setBorder(new EmptyBorder(11,14,11,11));fileTitle.setText("DIFF PREVIEW");fileTitle.setForeground(AtomTheme.SECONDARY);fileTitle.setFont(AtomTheme.UI.deriveFont(Font.BOLD,12f));fileTitle.setIcon(Icons.get("diff",AtomTheme.SECONDARY,16));previewHeader.add(fileTitle,BorderLayout.CENTER);
        JPanel previewTools=new JPanel(new FlowLayout(FlowLayout.RIGHT,7,0));previewTools.setOpaque(false);stagedDiff.setText("Staged");stagedDiff.setOpaque(false);stagedDiff.setForeground(AtomTheme.TEXT);previewTools.add(stagedDiff);JButton resolve=actionButton("Resolve","conflict",this::resolveConflict);previewTools.add(resolve);previewHeader.add(previewTools,BorderLayout.EAST);preview.add(previewHeader,BorderLayout.NORTH);preview.add(scroll(diff),BorderLayout.CENTER);
        JSplitPane reviewSplit=split(JSplitPane.HORIZONTAL_SPLIT,filePane,preview,.35);reviewSplit.setDividerLocation(355);changesPanel.add(reviewSplit,BorderLayout.CENTER);
        tabs.addTab("Log",Icons.get("history",AtomTheme.BLUE,16),gitLog);tabs.addTab("Working tree",Icons.get("diff",AtomTheme.SECONDARY,16),changesPanel);AtomTheme.tabs(tabs);editor.add(tabs,BorderLayout.CENTER);

        JPanel activity=new JPanel(new BorderLayout());activity.setBackground(AtomTheme.HEADER);JPanel activityHeader=new JPanel(new BorderLayout());activityHeader.setOpaque(false);activityHeader.setBorder(new EmptyBorder(8,14,8,12));JLabel activityTitle=sectionLabel("ACTIVITY");activityTitle.setIcon(Icons.get("terminal",AtomTheme.SECONDARY,15));activityHeader.add(activityTitle,BorderLayout.WEST);JLabel activityHint=new JLabel("Live output  /  no background scans");activityHint.setForeground(AtomTheme.MUTED);activityHint.setFont(AtomTheme.UI.deriveFont(12f));activityHeader.add(activityHint,BorderLayout.EAST);activity.add(activityHeader,BorderLayout.NORTH);output.setBackground(AtomTheme.HEADER);activity.add(scroll(output),BorderLayout.CENTER);
        activitySplit=split(JSplitPane.VERTICAL_SPLIT,editor,activity,.82);activitySplit.setDividerLocation(640);
        JSplitPane workspace=split(JSplitPane.HORIZONTAL_SPLIT,sidebarPanel,activitySplit,.21);workspace.setDividerLocation(285);
        JPanel work=new JPanel(new BorderLayout());work.add(rail,BorderLayout.WEST);work.add(workspace,BorderLayout.CENTER);canvas.add(work,BorderLayout.CENTER);
        JPanel footer=new JPanel(new BorderLayout(12,0));footer.setBackground(AtomTheme.HEADER);footer.setBorder(new EmptyBorder(7,14,7,14));branchTitle.setFont(AtomTheme.UI.deriveFont(12f));branchTitle.setForeground(AtomTheme.BLUE);branchTitle.setIcon(Icons.get("branch",AtomTheme.BLUE,14));footer.add(branchTitle,BorderLayout.WEST);status.setFont(AtomTheme.UI.deriveFont(12f));status.setForeground(AtomTheme.SECONDARY);footer.add(status,BorderLayout.CENTER);JLabel footerRight=new JLabel("Embedded Git  |  "+portable.profileName());footerRight.setFont(AtomTheme.UI.deriveFont(12f));footerRight.setForeground(AtomTheme.SECONDARY);footer.add(footerRight,BorderLayout.EAST);canvas.add(footer,BorderLayout.SOUTH);
        files.getSelectionModel().addListSelectionListener(e -> {if(!e.getValueIsAdjusting()&&!busy)showDiff();});stagedDiff.addActionListener(e -> showDiff());
        tabs.addChangeListener(e -> {if(tabs.getSelectedIndex()==0&&git!=null&&!busy)if(gitLog!=null)gitLog.update(git,currentBranch(),changes);});

        AtomTheme.polish(getContentPane());applyCodeFont();
        loadRepoChoices();
        String last=portable.settings.getProperty("lastRepo","");
        if(!last.isBlank()) SwingUtilities.invokeLater(() -> open(portable.resolve(last)));
    }
    public static void theme() throws Exception { AtomTheme.install(); }
    private void applyCodeFont(){TextRendering.selectMode(TextRendering.selected(portable.settings));TextRendering.configureTree(getRootPane());Font font=EditorFonts.current(portable.settings);diff.setCodeFont(font);history.setFont(font);output.setFont(font);revalidate();repaint();}
    private static JTextArea area(){JTextArea a=new JTextArea();a.setEditable(false);a.setFont(AtomTheme.CODE);a.setBackground(AtomTheme.EDITOR);a.setForeground(AtomTheme.TEXT);a.setCaretColor(AtomTheme.BRIGHT);a.setMargin(new Insets(10,13,10,13));a.setTabSize(4);return a;}
    private static JScrollPane scroll(Component component){JScrollPane p=new JScrollPane(component);p.setBorder(new EmptyBorder(0,0,0,0));p.getViewport().setBackground(component.getBackground());return p;}
    private JButton button(String text,Runnable action){JButton b=new JButton(text);AtomTheme.button(b,false);b.addActionListener(e -> action.run());actions.add(b);return b;}
    private JButton button(String text,String icon,Runnable action,boolean primary){JButton b=button(text,action);AtomTheme.button(b,primary);b.setIcon(Icons.get(icon,primary?AtomTheme.INK:AtomTheme.SECONDARY,16));return b;}
    private JButton actionButton(String text,String icon,Runnable action){JButton b=button(text,icon,action,false);b.getAccessibleContext().setAccessibleName(text.isBlank()?icon:text);return b;}
    private JButton railButton(String icon,String tooltip,Runnable action){JButton b=button("",icon,action,false);b.setBackground(AtomTheme.RAIL);b.setBorder(new EmptyBorder(10,8,10,8));b.setToolTipText(tooltip);b.getAccessibleContext().setAccessibleName(tooltip);b.setPreferredSize(new Dimension(39,40));return b;}
    private static JLabel sectionLabel(String text){JLabel l=new JLabel(text);l.setFont(AtomTheme.UI.deriveFont(Font.BOLD,12f));l.setForeground(AtomTheme.SECONDARY);return l;}
    private static JSplitPane split(int direction,Component first,Component second,double weight){JSplitPane pane=new JSplitPane(direction,first,second);pane.setResizeWeight(weight);pane.setDividerSize(3);pane.setBorder(new EmptyBorder(0,0,0,0));pane.setContinuousLayout(true);return pane;}
    private void toggleActivity(){Component bottom=activitySplit.getBottomComponent();boolean visible=bottom.isVisible();bottom.setVisible(!visible);activitySplit.setDividerSize(visible?0:3);activitySplit.setDividerLocation(visible?1.:.80);activitySplit.revalidate();}
    private void showSidebar(String card){sidebarCards.show(sidebarPanel,card);}
    private void showCommitDiff(String hash,String file){
        if(git==null||busy)return;
        task("Loading commit diff",() -> {},() -> {
            String text=(file==null||file.isBlank())?git.run("show","--stat","--patch",hash).require().out():git.run("show",hash,"--",file).require().out();
            String shown=text.length()>500_000?text.substring(0,500_000)+"\n[Display truncated]":text;
            ui(() -> {diff.setText(shown);diff.setCaretPosition(0);fileTitle.setText(hash.substring(0,Math.min(8,hash.length()))+(file!=null&&!file.isBlank()?" · "+file:""));tabs.setSelectedIndex(1);});
        });
    }
    private void repositoryMenu(){JPopupMenu menu=new JPopupMenu();String[] names={"Merge selected branch...","Save edits and merge...","Restore saved edits...","Abort current merge..."};Runnable[] commands={()->merge(false),()->merge(true),this::restoreShelf,this::abortMerge};for(int i=0;i<names.length;i++){JMenuItem item=new JMenuItem(names[i]);Runnable command=commands[i];item.addActionListener(e -> command.run());menu.add(item);}menu.show(this,getWidth()-265,111);}
    private void ui(Runnable action){SwingUtilities.invokeLater(action);}
    private void log(String text){ui(() -> {output.append(Git.redact(text)+"\n"); if(output.getDocument().getLength()>100_000) output.replaceRange("",0,25_000); output.setCaretPosition(output.getDocument().getLength());});}
    @FunctionalInterface private interface Job {void run() throws Exception;}
    private void task(String label,Runnable before,Job job){
        if(busy)return; busy=true; actions.forEach(b -> b.setEnabled(false)); files.setEnabled(false);branches.setEnabled(false); if(branchExplorer!=null)branchExplorer.setEnabled(false); stagedDiff.setEnabled(false); status.setText(label+"…"); before.run();
        jobs.submit(() -> {try{job.run();}catch(Exception e){log("ERROR: "+e.getMessage());ui(() -> JOptionPane.showMessageDialog(this,Git.redact(e.getMessage()==null?e.toString():e.getMessage()),"GitPort",JOptionPane.ERROR_MESSAGE));}
            finally{ui(() -> {busy=false;actions.forEach(b -> b.setEnabled(true));files.setEnabled(true);branches.setEnabled(true);if(branchExplorer!=null)branchExplorer.setEnabled(true);stagedDiff.setEnabled(true);status.setText("Ready · "+(git==null?"Choose a repository":git.repo));});}
        });
    }
    private boolean ready(){if(git!=null)return true; JOptionPane.showMessageDialog(this,"Open a repository first.");return false;}
    private void chooseRepo(){
        Path chosen=pickNativeFolder("Select Git repository",git!=null?git.repo:null);
        if(chosen!=null)open(chosen);
    }
    private Path pickNativeFolder(String title,Path initialFolder){
        Path picker=portable.root.resolve("FolderPicker.exe");
        if(!Files.exists(picker))picker=portable.root.resolve("tools/FolderPicker.exe");
        if(Files.exists(picker)){
            try{
                List<String> cmd=new ArrayList<>(List.of(picker.toString(),title));
                if(initialFolder!=null&&Files.isDirectory(initialFolder))cmd.add(initialFolder.toString());
                Process p=new ProcessBuilder(cmd).start();
                String out=new String(p.getInputStream().readAllBytes(),StandardCharsets.UTF_8).trim();
                int exit=p.waitFor();
                if(exit==0&&!out.isEmpty()){
                    Path pth=Path.of(out);
                    if(Files.isDirectory(pth))return pth;
                }else if(exit==1){
                    return null;
                }
            }catch(Exception ignored){}
        }
        JFileChooser chooser=new JFileChooser();AtomTheme.polish(chooser);chooser.setDialogTitle(title);chooser.setFileSelectionMode(JFileChooser.DIRECTORIES_ONLY);if(initialFolder!=null&&Files.isDirectory(initialFolder))chooser.setCurrentDirectory(initialFolder.toFile());if(chooser.showOpenDialog(this)==JFileChooser.APPROVE_OPTION)return chooser.getSelectedFile().toPath();
        return null;
    }
    private void loadRepoChoices(){if(explorer!=null)explorer.reload();}
    private void open(Path path){task("Opening repository",() -> {},() -> {
        Git candidate=new Git(portable,portable.git(),path); candidate.progress=this::log;
        Path root=Path.of(candidate.run("rev-parse","--show-toplevel").require().out().strip()); candidate.repo=root;
        Path askpass=portable.root.resolve("GitPortAskPass.exe");
        if(Files.exists(askpass))candidate.authentication.putAll(auth.environment(askpass));
        git=candidate;
        String relative=portable.portablePath(root); portable.settings.setProperty("lastRepo",relative);
        LinkedHashSet<String> recent=new LinkedHashSet<>();recent.add(relative);recent.addAll(Arrays.asList(portable.settings.getProperty("repos","").split("\n")));recent.remove("");
        portable.settings.setProperty("repos",String.join("\n",recent));portable.save();
        ui(() -> {loadRepoChoices();repoTitle.setText(root.getFileName().toString());repoPath.setText(root.toString());repoPath.setToolTipText(root.toString());setTitle("GitPort - "+root.getFileName());history.setText("");diff.setText("");showSidebar("branches");tabs.setSelectedIndex(0);});
        refreshNow();
        if(Files.exists(portable.data.resolve("pending-shelf.properties"))) log("Saved edits are pending. Use Restore saved edits after resolving or aborting any merge.");
    });}
    private void refresh(){if(ready())task("Refreshing tracked/untracked changes",() -> {},this::refreshNow);}
    private void refreshNow() throws Exception {
        List<Git.Change> next=git.changes();
        String rawBranch=git.run("symbolic-ref","--quiet","--short","HEAD").out().strip();
        String branch=rawBranch.isEmpty()?"Detached HEAD":rawBranch;
        String refs=git.run("for-each-ref","--format=%(refname:short)","refs/heads/","refs/remotes/").require().out();
        boolean merging=Files.exists(git.gitPath("MERGE_HEAD"));String title=branch+(merging?" · MERGING":"");
        List<Git.Worktree> currentWts = Collections.emptyList();
        try { currentWts = git.worktrees(); } catch (Exception ignored) {}
        final List<Git.Worktree> wts = currentWts;
        ui(() -> {changes=next;fileTitle.setText("DIFF PREVIEW");diff.setText("Select a changed file to review its diff.");filesModel.setRowCount(0);for(Git.Change c:next)filesModel.addRow(new Object[]{c.xy(),c.path()});
            String selected=selectedBranch();branchModel.clear();for(String ref:refs.split("\n"))if(!ref.isBlank()&&!ref.endsWith("/HEAD"))branchModel.addElement(ref.strip());
            if(branchExplorer!=null)branchExplorer.update(git.repo,branch,refs,wts);
            if(gitLog!=null)gitLog.update(git,branch,next);
            branchTitle.setText(title);changeCountLabel.setText(Integer.toString(next.size()));explorer.active(portable.portablePath(git.repo),title,next.size());});
        log(next.size()+" changed/untracked entries. Refresh is manual; no continuous repository polling.");
    }
    private List<Git.Change> selected(){List<Git.Change> selected=new ArrayList<>();for(int row:files.getSelectedRows())selected.add(changes.get(files.convertRowIndexToModel(row)));return selected;}
    private void showDiff(){
        if(busy||git==null)return;List<Git.Change> selection=selected();if(selection.isEmpty())return;
        Git.Change file=selection.getFirst();fileTitle.setText(file.path());fileTitle.setToolTipText(file.path()); boolean staged=stagedDiff.isSelected();
        task("Loading diff",() -> {},() -> {
            String text;
            if(file.xy().equals("??")) {
                Path path=safeFile(file.path());text=Files.isDirectory(path)?"Untracked directory. Stage it to include its contents.":readText(path);
            }else{
                List<String> args=new ArrayList<>(List.of("--literal-pathspecs","diff","--no-ext-diff","--no-textconv"));if(staged)args.add("--cached");args.add("--");args.add(file.path());
                text=git.run(args.toArray(String[]::new)).require().out();if(text.isBlank())text="No "+(staged?"staged":"unstaged")+" diff. Toggle Staged diff to view the other side.";
            }
            String shown=text.length()>500_000?text.substring(0,500_000)+"\n[Display truncated]":text;
            ui(() -> {diff.setText(shown);diff.setCaretPosition(0);});
        });
    }
    private void stage(boolean unstage){if(!ready())return;List<Git.Change> selection=selected();if(selection.isEmpty())return;
        task(unstage?"Unstaging":"Staging",() -> {},() -> {log((unstage?git.unstage(selection):git.stage(selection)).text());refreshNow();});}
    private void commit(){if(!ready())return;String text=message.getText();if(text.isBlank())return;
        task("Committing staged changes",() -> {},() -> {log(git.commit(text).text());ui(() -> message.setText(""));refreshNow();});}
    private String selectedBranch(){
        String ref=branchExplorer!=null?branchExplorer.selectedBranch():null;
        if(ref==null)ref=branches.getSelectedValue();
        return ref;
    }
    private boolean confirm(String text){return JOptionPane.showConfirmDialog(this,text,"GitPort",JOptionPane.OK_CANCEL_OPTION,JOptionPane.QUESTION_MESSAGE)==JOptionPane.OK_OPTION;}
    private void switchBranchTo(String ref){if(!ready()||ref==null)return;
        if(!confirm("Switch/Checkout "+ref+"?\nGit will refuse if it would overwrite local edits."))return;
        task("Switching to "+ref,() -> {},() -> {
            git.writable();List<String> args=new ArrayList<>();
            if(ref.matches("[0-9a-f]{7,40}")){args.addAll(List.of("checkout",ref));}
            else{args.add("switch");if(ref.startsWith("origin/"))args.add("--track");args.add(ref);}
            log(git.run(args.toArray(String[]::new)).require().text());refreshNow();
        });
    }
    private void pullBranch(String ref,boolean rebase){if(!ready()||ref==null)return;
        task("Pulling updates",() -> {},() -> {
            git.writable();List<String> cmd=new ArrayList<>(List.of("pull"));
            if(rebase)cmd.add("--rebase");
            if(ref.startsWith("origin/")){cmd.add("origin");cmd.add(ref.substring(7));}
            log(git.run(cmd.toArray(String[]::new)).require().text());refreshNow();
        });
    }
    private void rebaseOnto(String ref){if(!ready()||ref==null)return;
        if(!confirm("Rebase current branch onto "+ref+"?\nLocal commits will be reapplied on top of "+ref+"."))return;
        task("Rebasing onto "+ref,() -> {},() -> {
            git.writable();log(git.run("rebase",ref).require().text());refreshNow();
        });
    }
    private void compareWith(String ref){if(!ready()||ref==null)return;
        String cur=currentBranch();
        task("Comparing with "+ref,() -> {},() -> {
            String text=git.run("diff","--stat","--patch",ref+".."+cur).require().out();
            String shown=text.length()>500_000?text.substring(0,500_000)+"\n[Display truncated]":text;
            ui(() -> {
                diff.setText(shown.isBlank()?"No differences between "+ref+" and "+cur:shown);
                diff.setCaretPosition(0);fileTitle.setText("Compare: "+ref+" .. "+cur);tabs.setSelectedIndex(1);
            });
        });
    }
    private void pushUpToCommit(String hash){if(!ready()||hash==null)return;
        String cur=currentBranch();
        if(!confirm("Push commits up to "+hash.substring(0,Math.min(8,hash.length()))+" to origin/"+cur+"?"))return;
        network("push","origin",hash+":refs/heads/"+cur);
    }
    private void newTagAt(String hash){if(!ready()||hash==null)return;
        String tag=JOptionPane.showInputDialog(this,"New tag name for commit "+hash.substring(0,Math.min(8,hash.length())));
        if(tag==null||tag.isBlank())return;
        task("Creating tag",() -> {},() -> {
            Git.validateRef(tag);git.writable();log(git.run("tag",tag,hash).require().text());refreshNow();
        });
    }
    private void stashWorkspace(){if(!ready())return;
        task("Stashing workspace changes",() -> {},() -> {
            git.writable();log(git.run("stash","push","-u","-m","GitPort stash").require().text());refreshNow();
        });
    }
    private String currentBranch(){
        if(git==null)return "";
        try{return git.run("symbolic-ref","--quiet","--short","HEAD").out().strip();}catch(Exception e){return "HEAD";}
    }
    private void switchBranch(){String ref=selectedBranch();if(ref==null){JOptionPane.showMessageDialog(this,"Select a branch in the left panel.");return;}switchBranchTo(ref);}
    private void newBranchFrom(String baseRef){if(!ready())return;
        String prompt="New branch name"+(baseRef!=null&&!baseRef.isBlank()?" (from "+baseRef+")":" (from current HEAD)");
        String name=JOptionPane.showInputDialog(this,prompt);if(name==null)return;
        task("Creating branch",() -> {},() -> {Git.validateRef(name);git.writable();
            List<String> cmd=new ArrayList<>(List.of("switch","-c",name));
            if(baseRef!=null&&!baseRef.isBlank()&&!baseRef.equals(selectedBranch()))cmd.add(baseRef);
            log(git.run(cmd.toArray(String[]::new)).require().text());refreshNow();});}
    private void newBranch(){newBranchFrom(null);}
    private void newWorktreeFrom(String baseRef){if(!ready())return;
        List<String> branchList=new ArrayList<>();
        for(int i=0;i<branchModel.size();i++)branchList.add(branchModel.get(i));
        NewWorktreeDialog.show(this,git,baseRef,branchList,this::pickNativeFolder,this::open,this::log,msg -> JOptionPane.showMessageDialog(this,msg,"Worktree Error",JOptionPane.ERROR_MESSAGE));
    }
    private void mergeBranch(String ref,boolean fetchFirst){if(!ready()||ref==null)return;
        String explanation=(fetchFirst?"Fetch latest updates from remote, then merge "+ref+" into the current branch?":"Merge "+ref+" into the current branch?")+"\nUnrelated local edits stay in place. Git will refuse edits that block the merge.";
        if(!confirm(explanation))return;
        task(fetchFirst?"Fetching updates and merging":"Merging branch",() -> {},() -> {
            Git.Result result=fetchFirst?git.fetchAndMerge(ref,false):git.merge(ref);
            log(result.text());refreshNow();
            if(result.code()!=0)ui(() -> JOptionPane.showMessageDialog(this,"Git stopped the operation. Review Activity and Changes.\nNo reset or cleanup was performed.","Merge needs attention",JOptionPane.WARNING_MESSAGE));
        });
    }
    private void deleteBranchRef(String ref){if(!ready()||ref==null)return;
        if(!confirm("Delete branch "+ref+"?\nUnmerged commits on this branch will be lost."))return;
        task("Deleting branch",() -> {},() -> {git.writable();
            log(git.run("branch","-D",ref).require().text());refreshNow();});}
    private void removeWorktree(Git.Worktree wt){if(!ready()||wt==null||wt.isMain())return;
        if(!confirm("Remove worktree at "+wt.path()+"?\nUncommitted edits in that folder must be saved or stashed first."))return;
        task("Removing worktree",() -> {},() -> {git.writable();log(git.worktreeRemove(wt.path(),false).text());refreshNow();});}
    private void merge(boolean shelf){if(!ready())return;String ref=selectedBranch();if(ref==null){JOptionPane.showMessageDialog(this,"Select a branch in the left panel.");return;}
        String explanation=shelf?"Save tracked AND untracked edits to a retained Git stash, merge "+ref+", then restore edits?\nIgnored files are not saved. Restoration can require conflict resolution.":"Merge "+ref+" into the current branch?\nUnrelated local edits stay in place. Git will refuse edits that block the merge.";
        if(!confirm(explanation))return;
        task(shelf?"Saving edits and merging":"Merging branch",() -> {},() -> {Git.Result result=shelf?git.shelveAndMerge(ref):git.merge(ref);log(result.text());refreshNow();if(result.code()!=0)ui(() -> JOptionPane.showMessageDialog(this,"Git stopped the operation. Review Activity and Changes.\nNo reset or cleanup was performed.","Merge needs attention",JOptionPane.WARNING_MESSAGE));});
    }
    private void abortMerge(){if(!ready()||!confirm("Ask Git to abort the current merge?\nGit may be unable to reconstruct edits made during a merge."))return;
        task("Aborting merge",() -> {},() -> {git.writable();if(!Files.exists(git.gitPath("MERGE_HEAD")))throw new IOException("No active merge to abort.");log(git.run("merge","--abort").require().text());refreshNow();});}
    private void restoreShelf(){if(!ready())return;
        if(!confirm("Apply the pending saved edits to this repository?\nIf you already applied them and are resolving conflicts, Cancel.\nThe backup stash will be retained."))return;
        task("Restoring saved edits",() -> {},() -> {git.writable();Path file=portable.data.resolve("pending-shelf.properties");Properties p=new Properties();try(var in=Files.newInputStream(file)){p.load(in);}
            if(!portable.resolve(p.getProperty("repo")).toRealPath().equals(git.repo.toRealPath()))throw new IOException("The pending shelf belongs to another repository.");
            if(p.getProperty("phase","").startsWith("restore-"))throw new IOException("Restoration was already attempted. Do not apply the stash twice. Resolve the current edits, then review data/pending-shelf.properties using the README recovery instructions.");
            if(Files.exists(git.gitPath("MERGE_HEAD")))throw new IOException("Complete or abort the current merge first.");
            String oid=p.getProperty("oid","");if(!oid.matches("[0-9a-f]{40,64}"))throw new IOException("Shelf creation was interrupted. Review git stash list for "+p.getProperty("label")+" before recovery.");
            p.setProperty("phase","restore-started");Git.saveRecovery(file,p);
            Git.Result r=git.run("stash","apply","--index",oid);log(r.text());if(r.code()==0)Files.delete(file);else{p.setProperty("phase","restore-conflicts");Git.saveRecovery(file,p);}refreshNow();
        });
    }
    private void network(String... args){if(!ready())return;if(!confirm("Run git "+String.join(" ",args)+" for this repository?\nHTTPS tokens are sent only after an authentication prompt. Pull uses fast-forward only."))return;
        task("Running "+args[0],() -> {},() -> {git.writable();log(git.run(args).require().text());refreshNow();});}
    private Path safeFile(String name) throws IOException {
        Path root=git.repo.toRealPath(), file=root.resolve(name).normalize();if(!file.startsWith(root))throw new IOException("Path is outside the repository.");
        for(Path p=file;!p.equals(root);p=p.getParent())if(Files.isSymbolicLink(p))throw new IOException("Symbolic-link files are not edited by GitPort.");return file;
    }
    private static String readText(Path file) throws IOException {
        if(Files.size(file)>1_000_000)throw new IOException("File exceeds the 1 MB preview/edit limit. Use your editor.");
        byte[] bytes=Files.readAllBytes(file);for(byte b:bytes)if(b==0)throw new IOException("Binary files are not edited by GitPort.");
        return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(java.nio.ByteBuffer.wrap(bytes)).toString();
    }
    private void resolveConflict(){if(!ready())return;List<Git.Change> s=selected();if(s.size()!=1||!s.getFirst().conflict()){JOptionPane.showMessageDialog(this,"Select one conflicted text file.");return;}
        Git.Change change=s.getFirst();task("Loading conflict sides",() -> {},() -> {
            Path path=safeFile(change.path());String working=readText(path);byte[] before=Files.readAllBytes(path);
            String ours=git.run("show",":2:"+change.path()).require().out(), theirs=git.run("show",":3:"+change.path()).require().out();
            if(ours.indexOf(0)>=0||theirs.indexOf(0)>=0)throw new IOException("Binary conflict. Use an external merge tool.");
            ui(() -> conflictDialog(change,path,before,working,ours,theirs));
        });
    }
    private void conflictDialog(Git.Change change,Path path,byte[] before,String working,String ours,String theirs){
        JDialog dialog=new JDialog(this,"Resolve · "+change.path(),true);JTextArea left=area(),right=area(),result=area();left.setText(ours);right.setText(theirs);result.setText(working);result.setEditable(true);
        JPanel sides=new JPanel(new GridLayout(1,2,8,0));sides.add(labeled("OURS · current branch",left));sides.add(labeled("THEIRS · incoming branch",right));
        JSplitPane splitter=new JSplitPane(JSplitPane.VERTICAL_SPLIT,sides,labeled("RESULT · edit below, remove conflict markers",result));splitter.setResizeWeight(.45);
        JPanel container=new JPanel(new BorderLayout(8,8));container.setBorder(new EmptyBorder(12,12,12,12));container.add(splitter,BorderLayout.CENTER);
        JButton save=new JButton("Save result and stage"),cancel=new JButton("Cancel");JPanel buttons=new JPanel(new FlowLayout(FlowLayout.RIGHT));buttons.add(cancel);buttons.add(save);container.add(buttons,BorderLayout.SOUTH);
        cancel.addActionListener(e -> dialog.dispose());save.addActionListener(e -> {
            String text=result.getText();if(Pattern.compile("(?m)^(<<<<<<< |=======|>>>>>>> )").matcher(text).find()){JOptionPane.showMessageDialog(dialog,"Conflict markers remain. Resolve them before saving.");return;}
            if(busy)return;dialog.dispose();task("Saving conflict result",() -> {},() -> {
                git.writable();if(!Arrays.equals(before,Files.readAllBytes(safeFile(change.path()))))throw new IOException("File changed in another application. Reopen the resolver.");
                Path backup=git.gitPath("gitport-backups").resolve(UUID.randomUUID().toString());Files.createDirectories(backup);Files.write(backup.resolve("original"),before);Files.writeString(backup.resolve("path.txt"),change.path());
                Files.writeString(path,text,StandardCharsets.UTF_8);git.stage(List.of(change));log("Resolution saved and staged. Original kept in .git/gitport-backups.");refreshNow();
            });
        });AtomTheme.polish(container);Font font=EditorFonts.current(portable.settings);left.setFont(font);right.setFont(font);result.setFont(font);dialog.setContentPane(container);dialog.setSize(1100,760);dialog.setLocationRelativeTo(this);dialog.setVisible(true);
    }
    private static JPanel labeled(String label,JTextArea text){JPanel p=new JPanel(new BorderLayout(0,5));p.add(new JLabel(label),BorderLayout.NORTH);p.add(scroll(text),BorderLayout.CENTER);return p;}

    private String askCredential(String question){
        final String[] answer={""};try{SwingUtilities.invokeAndWait(() -> {
            String host="";boolean secure=false;try{Matcher match=Pattern.compile("https?://[^'\\s]+").matcher(question);if(match.find()){var origin=java.net.URI.create(match.group());host=origin.getHost();secure="https".equalsIgnoreCase(origin.getScheme());}}catch(Exception ignored){}
            String allowed=portable.settings.getProperty("credentialHost","");boolean matched=secure&&host!=null&&!host.isBlank()&&host.equalsIgnoreCase(allowed);
            if(question.toLowerCase(Locale.ROOT).contains("username")) {
                String user=matched?portable.settings.getProperty("username",""):"";
                if(user.isBlank())user=JOptionPane.showInputDialog(this,Git.redact(question),"Git HTTPS username",JOptionPane.QUESTION_MESSAGE);
                answer[0]=user==null?"":user;return;
            }
            if(matched&&!sessionToken.isBlank()){answer[0]=sessionToken;return;}
            Path vault=portable.data.resolve("credentials.vault");
            JPasswordField field=new JPasswordField(28);
            String label=matched&&Files.exists(vault)?"Unlock saved token for "+host+" with your vault password:":Git.redact(question)+"\nEnter a token/password for this operation:";
            if(JOptionPane.showConfirmDialog(this,new Object[]{label,field},"Git authentication",JOptionPane.OK_CANCEL_OPTION)!=JOptionPane.OK_OPTION)return;
            char[] chars=field.getPassword();try{
                if(matched&&Files.exists(vault)){sessionToken=Vault.decrypt(chars,Files.readAllBytes(vault));answer[0]=sessionToken;}
                else answer[0]=new String(chars);
            }catch(Exception ex){JOptionPane.showMessageDialog(this,"Cannot unlock vault. Check its password.");}finally{Arrays.fill(chars,'\0');field.setText("");}
        });}catch(Exception ignored){}return answer[0];
    }
    private void settings(){
        JTextField gitFile=new JTextField(portable.settings.getProperty("git","git/cmd/git.exe"),35),name=new JTextField(portable.settings.getProperty("name","")),email=new JTextField(portable.settings.getProperty("email","")),username=new JTextField(portable.settings.getProperty("username","")),host=new JTextField(portable.settings.getProperty("credentialHost","")),ssh=new JTextField(portable.settings.getProperty("sshKey",""));
        JPasswordField token=new JPasswordField(),password=new JPasswordField();
        FontSettings fonts=new FontSettings(portable.settings);
        JPanel fields=settingsForm(gitFile,name,email,username,host,token,password,ssh,fonts);
        if(JOptionPane.showConfirmDialog(this,fields,"Profile settings",JOptionPane.OK_CANCEL_OPTION,JOptionPane.PLAIN_MESSAGE)!=JOptionPane.OK_OPTION)return;
        try{fonts.size.commitEdit();}catch(java.text.ParseException e){JOptionPane.showMessageDialog(this,"Enter a font size from 11 to 22.");return;}
        String editorFamily=fonts.selectedFamily();int editorSize=fonts.selectedSize();
        String textRendering=fonts.selectedRendering().name();
        char[] secret=token.getPassword(),pass=password.getPassword();String secretValue=new String(secret);Arrays.fill(secret,'\0');
        task("Saving private app settings",() -> {},() -> {try{
            if(!secretValue.isBlank()&&(pass.length<12||host.getText().isBlank()))throw new IOException("A token requires its exact host and a vault password of at least 12 characters.");
            String oldHost=portable.settings.getProperty("credentialHost","");
            if(!oldHost.equalsIgnoreCase(host.getText().strip())&&Files.exists(portable.data.resolve("credentials.vault"))&&secretValue.isBlank())throw new IOException("Changing a credential host requires a new token. The existing vault was not changed.");
            if(!secretValue.isBlank())Vault.save(portable.data.resolve("credentials.vault"),pass,secretValue);
            portable.settings.setProperty("git",gitFile.getText().strip());portable.settings.setProperty("name",name.getText().strip());portable.settings.setProperty("email",email.getText().strip());portable.settings.setProperty("username",username.getText().strip());portable.settings.setProperty("credentialHost",host.getText().strip());portable.settings.setProperty("sshKey",ssh.getText().strip());portable.settings.setProperty("editorFont",editorFamily);portable.settings.setProperty("editorFontSize",Integer.toString(editorSize));portable.settings.setProperty("textRendering",textRendering);portable.save();sessionToken="";ui(this::applyCodeFont);
            Git config=new Git(portable,portable.git(),portable.root);config.progress=s -> {};
            if(!name.getText().isBlank())config.run("config","--file",portable.data.resolve("gitconfig").toString(),"user.name",name.getText().strip()).require();
            if(!email.getText().isBlank())config.run("config","--file",portable.data.resolve("gitconfig").toString(),"user.email",email.getText().strip()).require();
            log("Settings saved beside GitPort. Reopen your repository to use changed Git/SSH paths.");
        }finally{Arrays.fill(pass,'\0');}});
    }
    private JPanel settingsForm(JTextField gitFile,JTextField name,JTextField email,JTextField username,JTextField host,JPasswordField token,JPasswordField password,JTextField ssh,FontSettings fonts){
        JPanel panel=new JPanel(new BorderLayout(0,14));panel.setBorder(new EmptyBorder(9,8,7,8));panel.setPreferredSize(new Dimension(610,550));
        JPanel heading=new JPanel(new GridLayout(0,1,0,6));heading.setOpaque(false);JLabel title=new JLabel("Profile settings");title.setFont(AtomTheme.UI.deriveFont(Font.BOLD,20f));title.setForeground(AtomTheme.BRIGHT);heading.add(title);JLabel subtitle=new JLabel("Saved only in the current profile. Other profiles stay independent.");subtitle.setForeground(AtomTheme.SECONDARY);heading.add(subtitle);panel.add(heading,BorderLayout.NORTH);
        JTabbedPane sections=new JTabbedPane();
        sections.addTab("Identity",Icons.get("profile",AtomTheme.BLUE,16),formPanel("Your commit identity","This name and email identify commits. They are separate from your remote login.",new String[]{"Author name","Author email"},new JComponent[]{name,email}));
        sections.addTab("Authentication",Icons.get("shelf",AtomTheme.SECONDARY,16),formPanel("Remote account","One HTTPS account per profile. Leave the token blank to keep the existing encrypted vault.",new String[]{"HTTPS host (for example github.com)","Login username","New access token","Vault password for a new token (12+ characters)","SSH private key path (optional)"},new JComponent[]{host,username,token,password,ssh}));
        sections.addTab("Runtime",Icons.get("settings",AtomTheme.SECONDARY,16),formPanel("Self-contained Git runtime","Git is included with GitPort. Change this path only when choosing a different complete Git distribution. Relative paths travel with the app.",new String[]{"Git executable","SSH host verification"},new JComponent[]{gitFile,new JLabel("Verified host keys are stored in this profile's known_hosts file.")}));
        sections.addTab("Appearance",Icons.get("diff",AtomTheme.SECONDARY,16),fonts);
        Component[] pages=new Component[sections.getTabCount()];String[] titles=new String[pages.length];Icon[] icons=new Icon[pages.length];
        for(int i=0;i<pages.length;i++){pages[i]=sections.getComponentAt(i);titles[i]=sections.getTitleAt(i);icons[i]=sections.getIconAt(i);}sections.removeAll();
        for(int i=0;i<pages.length;i++)sections.addTab(titles[i],icons[i],scroll(pages[i]));
        AtomTheme.tabs(sections);panel.add(sections,BorderLayout.CENTER);AtomTheme.polish(panel);fonts.refreshPreview();return panel;
    }
    private JPanel formPanel(String title,String description,String[] labels,JComponent[] fields){
        JPanel form=new JPanel();form.setLayout(new BoxLayout(form,BoxLayout.Y_AXIS));form.setBorder(new EmptyBorder(17,12,12,12));form.setBackground(AtomTheme.EDITOR);
        JLabel heading=new JLabel(title);heading.setFont(AtomTheme.UI.deriveFont(Font.BOLD,15f));heading.setForeground(AtomTheme.BRIGHT);heading.setAlignmentX(Component.LEFT_ALIGNMENT);form.add(heading);form.add(Box.createVerticalStrut(7));
        JTextArea details=new JTextArea(description);details.setFont(AtomTheme.UI.deriveFont(12f));details.setForeground(AtomTheme.SECONDARY);details.setBackground(AtomTheme.EDITOR);details.setEditable(false);details.setLineWrap(true);details.setWrapStyleWord(true);details.setBorder(new EmptyBorder(0,0,0,0));details.setAlignmentX(Component.LEFT_ALIGNMENT);details.setMaximumSize(new Dimension(Integer.MAX_VALUE,42));details.setPreferredSize(new Dimension(530,42));form.add(details);form.add(Box.createVerticalStrut(12));
        for(int i=0;i<labels.length;i++){JLabel label=new JLabel(labels[i]);label.setForeground(AtomTheme.TEXT);label.setAlignmentX(Component.LEFT_ALIGNMENT);label.setLabelFor(fields[i]);form.add(label);form.add(Box.createVerticalStrut(5));fields[i].setAlignmentX(Component.LEFT_ALIGNMENT);fields[i].setMaximumSize(new Dimension(Integer.MAX_VALUE,32));fields[i].setPreferredSize(new Dimension(530,32));form.add(fields[i]);form.add(Box.createVerticalStrut(12));}form.add(Box.createVerticalGlue());return form;
    }
    private record Profile(String id,String name){@Override public String toString(){return name;}}
    private void profiles(){
        if(busy)return;
        try{
            Properties catalog=Portable.catalog(portable.root);List<Profile> profiles=new ArrayList<>();
            for(String key:catalog.stringPropertyNames())if(key.startsWith("profile."))profiles.add(new Profile(key.substring(8),catalog.getProperty(key)));
            profiles.sort(Comparator.comparing(Profile::name,String.CASE_INSENSITIVE_ORDER));
            JComboBox<Profile> chooser=new JComboBox<>(profiles.toArray(Profile[]::new));
            for(Profile p:profiles)if(p.id.equals(portable.profileId))chooser.setSelectedItem(p);
            Object[] content={"Each profile has its own repositories, author identity, HTTPS vault and SSH settings.",chooser,"Switching locks the current profile's vault. No repository is modified by switching."};
            int choice=JOptionPane.showOptionDialog(this,content,"Workspace profiles",JOptionPane.DEFAULT_OPTION,JOptionPane.PLAIN_MESSAGE,null,new String[]{"Switch profile","Create profile","Cancel"},"Switch profile");
            String id;
            if(choice==1){String name=JOptionPane.showInputDialog(this,"New profile name (e.g. Personal, Company, Client A)");if(name==null)return;id=Portable.createProfile(portable.root,name);}
            else if(choice==0&&chooser.getSelectedItem()!=null)id=((Profile)chooser.getSelectedItem()).id;
            else return;
            if(id.equals(portable.profileId))return;
            Portable.activate(portable.root,id);Portable next=new Portable(portable.root,id);
            System.setProperty("user.home",next.data.toString());System.setProperty("java.io.tmpdir",next.temp.toString());
            sessionToken="";auth.close();instanceLock.release();instanceChannel.close();jobs.shutdown();dispose();
            new App(next).setVisible(true);
        }catch(Exception e){JOptionPane.showMessageDialog(this,e.getMessage(),"Profiles",JOptionPane.ERROR_MESSAGE);}
    }
}





