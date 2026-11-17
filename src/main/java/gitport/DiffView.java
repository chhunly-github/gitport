package gitport;

import javax.swing.*;
import javax.swing.text.*;
import java.awt.*;

public final class DiffView extends JTextPane {
    public DiffView(){setEditable(false);setFont(AtomTheme.CODE);setBackground(AtomTheme.EDITOR);setForeground(AtomTheme.TEXT);setBorder(new javax.swing.border.EmptyBorder(14,16,14,16));setText("Select a changed file to review its diff.\n\nStage the changes you want to commit; unrelated edits stay in your working tree.");}
    @Override public void setText(String text){
        if(text==null)text="";StyledDocument doc=getStyledDocument();try{doc.remove(0,doc.getLength());
            int count=0;for(String line:text.split("\n",-1)){
                SimpleAttributeSet attrs=new SimpleAttributeSet();StyleConstants.setFontFamily(attrs,getFont().getFamily());StyleConstants.setFontSize(attrs,getFont().getSize());StyleConstants.setForeground(attrs,AtomTheme.TEXT);StyleConstants.setBackground(attrs,AtomTheme.EDITOR);
                if(line.startsWith("+")&&!line.startsWith("+++")){StyleConstants.setForeground(attrs,AtomTheme.ADDED_TEXT);StyleConstants.setBackground(attrs,AtomTheme.ADDED_BG);}
                else if(line.startsWith("-")&&!line.startsWith("---")){StyleConstants.setForeground(attrs,AtomTheme.DELETED_TEXT);StyleConstants.setBackground(attrs,AtomTheme.DELETED_BG);}
                else if(line.startsWith("@@")||line.startsWith("diff ")||line.startsWith("index "))StyleConstants.setForeground(attrs,AtomTheme.BLUE);
                else if(line.startsWith("+++")||line.startsWith("---")){StyleConstants.setForeground(attrs,AtomTheme.SECONDARY);StyleConstants.setBold(attrs,true);}
                doc.insertString(doc.getLength(),line+"\n",attrs);if(++count>6000){doc.insertString(doc.getLength(),"\n[Diff display truncated at 6,000 lines]\n",attrs);break;}
            }
        }catch(BadLocationException e){throw new IllegalStateException(e);}setCaretPosition(0);
    }
    public void setCodeFont(Font font){
        setFont(font);SimpleAttributeSet attrs=new SimpleAttributeSet();StyleConstants.setFontFamily(attrs,font.getFamily());StyleConstants.setFontSize(attrs,font.getSize());
        getStyledDocument().setCharacterAttributes(0,getDocument().getLength(),attrs,false);
    }
    @Override public boolean getScrollableTracksViewportWidth(){return getParent()!=null&&getUI().getPreferredSize(this).width<=getParent().getSize().width;}
}
